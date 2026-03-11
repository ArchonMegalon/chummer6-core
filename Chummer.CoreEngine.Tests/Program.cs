using Chummer.Application.AI;
using Chummer.Application.BuildLab;
using Chummer.Application.Content;
using Chummer.Application.Explain;
using Chummer.Application.Hub;
using Chummer.Application.Journal;
using Chummer.Application.Session;
using Chummer.Application.Validation;
using Chummer.Contracts;
using Chummer.Contracts.AI;
using Chummer.Contracts.BuildLab;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Journal;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Validation;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr4;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using System.Text.Json;
using System.Text.Json.Serialization;

return CoreEngineTests.Run();

internal static class CoreEngineTests
{
    public static int Run()
    {
        try
        {
            CapabilityDescriptorsEmitLocalizationKeys();
            ExperimentalRulesetsEmitDiagnosticMessageKeys();
            SessionReplayDiagnosticsStayKeyed();
            SelectionAndFilterDisabledReasonsStayKeyed();
            SessionEventCompatibilityContractsRoundTripToCanonicalEnvelope();
            RuntimeInspectorProjectsCapabilityAndCompatibilityKeys();
            RuntimeInspectorProjectionIsDeterministicAcrossPackAndBindingOrder();
            RuntimeLockDiffIsDeterministicAndParameterized();
            AiExplainProjectionEmitsStructuredProvenance();
            AiExplainProjectionPrefersTraceContextOverMismatchedSessionContext();
            ContractGoldenJsonFixturesStayStable();
            ContractNormalizationFixturesStayStable();
            RepoBoundaryGuardsHostedContractsAndSharedContractOwnership();
            ActiveCoreEngineSolutionStaysPurified();
            HardeningBacklogStaysMilestoneMapped();
            LocalizationFallbackHelpersNormalizeLegacyContracts();
            SessionAndRuntimeCompatibilityProjectionsStayDeterministic();
            JournalProjectionIsDeterministicAndValidated();
            ValidationSummaryAndExplainHookCompositionStayDeterministic();
            BuildLabOutputsAreDeterministicAndLocalized();
            ContentInstallPreviewsEmitLocalizationKeys();
            Console.WriteLine("core-engine-tests: ok");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CapabilityDescriptorsEmitLocalizationKeys()
    {
        RulesetCapabilityDescriptor explicitDescriptor = new(
            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
            InvocationKind: RulesetCapabilityInvocationKinds.Script,
            Title: "Session Quick Actions",
            Explainable: true,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(1_000, 5_000, 1_024),
            MaximumGasBudget: new RulesetGasBudget(2_000, 10_000, 2_048),
            TitleKey: "ruleset.capability.session.quick-actions.title",
            TitleParameters: []);
        RulesetCapabilityDescriptor fallbackDescriptor = new(
            CapabilityId: RulePackCapabilityIds.DeriveStat,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            Title: "Derived Stat Evaluation",
            Explainable: true,
            SessionSafe: false,
            DefaultGasBudget: new RulesetGasBudget(1_000, 5_000, 1_024));

        AssertEx.Equal(
            "ruleset.capability.session.quick-actions.title",
            RulesetCapabilityDescriptorLocalization.ResolveTitleKey(explicitDescriptor),
            "Explicit capability title keys should be preserved.");
        AssertEx.Equal(
            "ruleset.capability.derive.stat.title",
            RulesetCapabilityDescriptorLocalization.ResolveTitleKey(fallbackDescriptor),
            "Descriptors without an explicit title key should fall back to a capability-scoped key.");
        AssertEx.Equal(
            0,
            RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(explicitDescriptor).Count,
            "Descriptors should expose deterministic title parameter collections.");
    }

    private static void ExperimentalRulesetsEmitDiagnosticMessageKeys()
    {
        Sr4RulesetPlugin sr4 = new();
        Sr6RulesetPlugin sr6 = new();

        RulesetCapabilityInvocationResult sr4Result = sr4.Capabilities
            .InvokeAsync(
                new RulesetCapabilityInvocationRequest(
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                    Arguments: []),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        RulesetCapabilityInvocationResult sr6Result = sr6.Capabilities
            .InvokeAsync(
                new RulesetCapabilityInvocationRequest(
                    CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                    InvocationKind: RulesetCapabilityInvocationKinds.Script,
                    Arguments: []),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.Equal("sr4.rule.experimental", sr4Result.Diagnostics[0].MessageKey, "SR4 rule diagnostics should expose localization keys.");
        AssertEx.Equal("sr6.script.experimental", sr6Result.Diagnostics[0].MessageKey, "SR6 script diagnostics should expose localization keys.");
    }

    private static void SessionReplayDiagnosticsStayKeyed()
    {
        DefaultSessionOverlayProjectionService service = new();
        SessionOverlayProjection projection = service.Replay(
            overlayId: "overlay-1",
            characterId: "char-1",
            runtimeFingerprint: "sha256:test",
            events:
            [
                new SessionEventEnvelope(
                    EventId: "evt-2",
                    OverlayId: "overlay-1",
                    BaseCharacterVersion: new CharacterVersionReference("char-1", "ver-1", RulesetDefaults.Sr5, "sha256:test"),
                    DeviceId: "device-1",
                    ActorId: "actor-1",
                    Sequence: 2,
                    EventType: SessionOverlayEventKinds.TrackerIncrement,
                    Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
                    {
                        ["absoluteValue"] = RulesetCapabilityBridge.FromObject(2)
                    },
                    CreatedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(2)),
                new SessionEventEnvelope(
                    EventId: "evt-1",
                    OverlayId: "overlay-1",
                    BaseCharacterVersion: new CharacterVersionReference("char-1", "ver-1", RulesetDefaults.Sr5, "sha256:test"),
                    DeviceId: "device-1",
                    ActorId: "actor-1",
                    Sequence: 1,
                    EventType: SessionOverlayEventKinds.TrackerIncrement,
                    Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal),
                    CreatedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(1))
            ]);

        AssertEx.True(
            projection.Diagnostics.Any(diagnostic => string.Equals(diagnostic.MessageKey, "session.replay.absolute-write-blocked", StringComparison.Ordinal)),
            "Session replay should keep absolute-write diagnostics keyed.");
        AssertEx.True(
            projection.Diagnostics.Any(diagnostic => string.Equals(diagnostic.MessageKey, "session.replay.tracker.missing-id", StringComparison.Ordinal)),
            "Session replay should keep missing tracker identifiers keyed.");
    }

    private static void SelectionAndFilterDisabledReasonsStayKeyed()
    {
        DisabledReasonPayload disabledReason = new(
            ReasonKey: "ruleset.disabled.availability",
            ReasonParameters:
            [
                new RulesetExplainParameter("required", RulesetCapabilityBridge.FromObject(12)),
                new RulesetExplainParameter("actual", RulesetCapabilityBridge.FromObject(8))
            ]);
        FilterChoicesOutput filterOutput = new(
            EnabledIds: ["item-a"],
            DisabledReasons: new Dictionary<string, DisabledReasonPayload>(StringComparer.Ordinal)
            {
                ["item-b"] = disabledReason
            });
        SessionQuickActionOutput quickActionOutput = new(
            Allowed: false,
            DisabledReason: disabledReason,
            Diagnostics: []);
        SessionQuickActionDescriptor sessionAction = new(
            ActionId: "heal",
            Label: "Quick Heal",
            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
            IsEnabled: false,
            DisabledReasonKey: "session.quick-action.cooldown",
            DisabledReasonParameters:
            [
                new RulesetExplainParameter("remaining", RulesetCapabilityBridge.FromObject(2))
            ]);
        HubProjectAction hubAction = new(
            ActionId: "apply",
            Label: "Apply",
            Kind: HubProjectActionKinds.Apply,
            Enabled: false,
            DisabledReasonKey: "hub.action.blocked.missing-runtime",
            DisabledReasonParameters:
            [
                new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:missing"))
            ]);
        AiHubProjectActionProjection aiAction = new(
            ActionId: "apply",
            Label: "Apply",
            Kind: HubProjectActionKinds.Apply,
            Enabled: false,
            DisabledReasonKey: "hub.action.blocked.missing-runtime",
            DisabledReasonParameters:
            [
                new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:missing"))
            ]);

        AssertEx.Equal(
            "ruleset.disabled.availability",
            DisabledReasonPayloadLocalization.ResolveReasonKey(filterOutput.DisabledReasons["item-b"]),
            "Filter choice disabled reasons should remain keyed.");
        AssertEx.Equal(
            "ruleset.disabled.availability",
            DisabledReasonPayloadLocalization.ResolveReasonKey(quickActionOutput.DisabledReason!),
            "Session quick-action capability outputs should remain keyed.");
        AssertEx.Equal(
            "session.quick-action.cooldown",
            SessionProjectionContractLocalization.ResolveDisabledReasonKey(sessionAction),
            "Session quick-action projections should resolve disabled reason keys.");
        AssertEx.Equal(
            "hub.action.blocked.missing-runtime",
            HubProjectDetailContractLocalization.ResolveDisabledReasonKey(hubAction),
            "Hub action projections should resolve disabled reason keys.");
        AssertEx.Equal(
            "hub.action.blocked.missing-runtime",
            AiHubProjectSearchContractLocalization.ResolveDisabledReasonKey(aiAction),
            "AI hub action projections should resolve disabled reason keys.");
        AssertEx.True(
            SessionProjectionContractLocalization.ResolveDisabledReasonParameters(sessionAction).Count == 1
            && HubProjectDetailContractLocalization.ResolveDisabledReasonParameters(hubAction).Count == 1
            && AiHubProjectSearchContractLocalization.ResolveDisabledReasonParameters(aiAction).Count == 1,
            "Disabled reason key payloads should preserve parameter envelopes across selection/filter projections.");
    }

    private static void SessionEventCompatibilityContractsRoundTripToCanonicalEnvelope()
    {
        CharacterVersionReference baseCharacterVersion = new("char-1", "ver-1", RulesetDefaults.Sr5, "sha256:test");
#pragma warning disable CS0618
        SessionEvent legacyEvent = new(
            EventId: "evt-legacy",
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            DeviceId: "device-1",
            ActorId: "actor-1",
            Sequence: 7,
            EventType: SessionEventTypes.TrackerIncrement,
            PayloadJson: "{\"trackerId\":\"stun\",\"amount\":2}",
            CreatedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(7));

        SessionEventEnvelope envelope = legacyEvent.ToEnvelope();
        SessionEvent roundTrippedLegacy = SessionEvent.FromEnvelope(envelope);
#pragma warning restore CS0618

        AssertEx.Equal(
            SessionEventEnvelopeSchemas.SessionEventsVnext,
            envelope.Schema,
            "Canonical session event envelopes should publish the vNext schema marker.");
        AssertEx.Equal(
            "stun",
            envelope.Payload["trackerId"].StringValue,
            "Compatibility payload parsing should preserve string fields.");
        AssertEx.Equal(
            2L,
            envelope.Payload["amount"].IntegerValue.GetValueOrDefault(),
            "Compatibility payload parsing should preserve numeric fields.");
        AssertEx.Equal(
            "{\"trackerId\":\"stun\",\"amount\":2}",
            roundTrippedLegacy.PayloadJson,
            "Compatibility wrappers should round-trip canonical envelopes back to the legacy JSON payload shape.");
    }

    private static void RuntimeInspectorProjectsCapabilityAndCompatibilityKeys()
    {
        DefaultRuntimeInspectorService service = new(
            new RulesetPluginRegistry([new Sr5RulesetPlugin(), new Sr6RulesetPlugin()]),
            new RuleProfileRegistryServiceStub(CreateProfile()),
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "1.0.0",
                        Title: "House Rules",
                        Author: "GM",
                        Description: "Campaign overlay.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets: [],
                        Capabilities: [],
                        ExecutionPolicies: []),
                    new RulePackPublicationMetadata(
                        OwnerId: "local-single-user",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "official.sr5.core", RulesetDefaults.Sr5);

        AssertEx.NotNull(projection, "Runtime inspector should resolve the seeded profile.");
        RuntimeInspectorProjection resolvedProjection = projection!;
        IReadOnlyList<RuntimeInspectorCapabilityDescriptorProjection> capabilityDescriptors = resolvedProjection.CapabilityDescriptors ?? [];
        AssertEx.True(
            capabilityDescriptors.Any(descriptor =>
                string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal)
                && string.Equals(descriptor.TitleKey, "ruleset.capability.derive.stat.title", StringComparison.Ordinal)),
            "Runtime inspector should surface capability title keys.");
        AssertEx.True(
            resolvedProjection.CompatibilityDiagnostics.Any(diagnostic =>
                string.Equals(diagnostic.MessageKey, "runtime.lock.compatibility.compatible", StringComparison.Ordinal)),
            "Runtime inspector should surface compatibility message keys.");
        AssertEx.True(
            resolvedProjection.CompatibilityDiagnostics.All(diagnostic =>
                string.Equals(diagnostic.Message, diagnostic.MessageKey, StringComparison.Ordinal)
                && diagnostic.MessageParameters is { Count: > 0 }),
            "Runtime inspector compatibility diagnostics should keep keyed message payloads.");
        AssertEx.True(
            resolvedProjection.Warnings.All(warning =>
                string.Equals(warning.Message, warning.MessageKey, StringComparison.Ordinal)
                && warning.MessageParameters is { Count: > 0 }),
            "Runtime inspector warnings should keep keyed message payloads.");
        AssertEx.True(
            resolvedProjection.MigrationPreview.All(item =>
                string.Equals(item.Summary, item.SummaryKey, StringComparison.Ordinal)
                && item.SummaryParameters is { Count: > 0 }),
            "Runtime inspector migration previews should keep keyed summary payloads.");
    }

    private static void RuntimeLockDiffIsDeterministicAndParameterized()
    {
        DefaultRuntimeLockDiffService service = new();
        ResolvedRuntimeLock before = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-1",
                    Title: "SR5 Base",
                    Description: "Built-in base content.",
                    AssetPaths: ["lang/", "data/"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("house-rules", "1.0.0"),
                new ArtifactVersionReference("gm-overrides", "1.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.ValidateCharacter] = "house-rules/validate.character",
                [RulePackCapabilityIds.ContentCatalog] = "gm-overrides/content.catalog"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "sha256:before");
        ResolvedRuntimeLock afterA = new(
            RulesetId: RulesetDefaults.Sr6,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "campaign.seattle",
                    RulesetId: RulesetDefaults.Sr6,
                    Version: "2026.03",
                    Title: "Seattle Campaign",
                    Description: "Campaign bundle.",
                    AssetPaths: ["data/", "media/"]),
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-1",
                    Title: "SR5 Base",
                    Description: "Built-in base content.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("gm-overrides", "1.0.0"),
                new ArtifactVersionReference("seattle-tools", "2.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.ContentCatalog] = "seattle-tools/content.catalog",
                [RulePackCapabilityIds.ValidateCharacter] = "house-rules/validate.character.v2"
            },
            EngineApiVersion: "rulepack-v2",
            RuntimeFingerprint: "sha256:after-a");
        ResolvedRuntimeLock afterB = afterA with
        {
            ContentBundles =
            [
                afterA.ContentBundles[1],
                afterA.ContentBundles[0]
            ],
            RulePacks =
            [
                afterA.RulePacks[1],
                afterA.RulePacks[0]
            ],
            ProviderBindings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.ValidateCharacter] = "house-rules/validate.character.v2",
                [RulePackCapabilityIds.ContentCatalog] = "seattle-tools/content.catalog"
            },
            RuntimeFingerprint = "sha256:after-b"
        };

        RuntimeLockDiffProjection diffA = service.Diff(before, afterA);
        RuntimeLockDiffProjection diffB = service.Diff(before, afterB);

        AssertEx.SequenceEqual(
            diffA.Changes.Select(ToComparableChange),
            diffB.Changes.Select(ToComparableChange),
            "Runtime-lock diffs should remain stable across input ordering.");
        AssertEx.True(
            diffA.Changes.All(change => change.ReasonParameters.Count > 0),
            "Runtime-lock diffs should carry structured reason parameters for every change.");
        AssertEx.SequenceEqual(
            diffA.Changes.Select(change => change.Kind),
            [
                RuntimeLockDiffChangeKinds.RulesetChanged,
                RuntimeLockDiffChangeKinds.EngineApiChanged,
                RuntimeLockDiffChangeKinds.ContentBundleAdded,
                RuntimeLockDiffChangeKinds.RulePackAdded,
                RuntimeLockDiffChangeKinds.RulePackRemoved,
                RuntimeLockDiffChangeKinds.ProviderBindingChanged,
                RuntimeLockDiffChangeKinds.ProviderBindingChanged
            ],
            "Runtime-lock diffs should emit changes in a deterministic kind order.");
    }

    private static void RuntimeInspectorProjectionIsDeterministicAcrossPackAndBindingOrder()
    {
        DefaultRuntimeInspectorService service = new(
            new RulesetPluginRegistry([new Sr5RulesetPlugin()]),
            new RuleProfileRegistryServiceStub(
            [
                CreateDeterministicInspectorProfile(
                    "deterministic-a",
                    [
                        new RuleProfilePackSelection(new ArtifactVersionReference("house-rules", "1.0.0"), Required: true, EnabledByDefault: true),
                        new RuleProfilePackSelection(new ArtifactVersionReference("house", "1.0.0"), Required: false, EnabledByDefault: true),
                        new RuleProfilePackSelection(new ArtifactVersionReference("missing-zeta", "1.0.0"), Required: false, EnabledByDefault: false),
                        new RuleProfilePackSelection(new ArtifactVersionReference("missing-alpha", "1.0.0"), Required: false, EnabledByDefault: false)
                    ],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RulePackCapabilityIds.ValidateCharacter] = "house-rules/validate.character",
                        [RulePackCapabilityIds.ContentCatalog] = "house-rules/content.catalog"
                    }),
                CreateDeterministicInspectorProfile(
                    "deterministic-b",
                    [
                        new RuleProfilePackSelection(new ArtifactVersionReference("missing-alpha", "1.0.0"), Required: false, EnabledByDefault: false),
                        new RuleProfilePackSelection(new ArtifactVersionReference("house", "1.0.0"), Required: false, EnabledByDefault: true),
                        new RuleProfilePackSelection(new ArtifactVersionReference("missing-zeta", "1.0.0"), Required: false, EnabledByDefault: false),
                        new RuleProfilePackSelection(new ArtifactVersionReference("house-rules", "1.0.0"), Required: true, EnabledByDefault: true)
                    ],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RulePackCapabilityIds.ContentCatalog] = "house-rules/content.catalog",
                        [RulePackCapabilityIds.ValidateCharacter] = "house-rules/validate.character"
                    })
            ]),
            new RulePackRegistryServiceStub(
            [
                CreateRulePackEntry(
                    packId: "house-rules",
                    capabilities:
                    [
                        new RulePackCapabilityDescriptor(RulePackCapabilityIds.ValidateCharacter, RulePackAssetKinds.Lua, RulePackAssetModes.AddProvider),
                        new RulePackCapabilityDescriptor(RulePackCapabilityIds.ContentCatalog, RulePackAssetKinds.Xml, RulePackAssetModes.MergeCatalog)
                    ]),
                CreateRulePackEntry(
                    packId: "house",
                    capabilities:
                    [
                        new RulePackCapabilityDescriptor(RulePackCapabilityIds.SessionQuickActions, RulePackAssetKinds.Lua, RulePackAssetModes.AddProvider)
                    ])
            ]));

        RuntimeInspectorProjection? projectionA = service.GetProfileProjection(OwnerScope.LocalSingleUser, "deterministic-a", RulesetDefaults.Sr5);
        RuntimeInspectorProjection? projectionB = service.GetProfileProjection(OwnerScope.LocalSingleUser, "deterministic-b", RulesetDefaults.Sr5);

        AssertEx.NotNull(projectionA, "Runtime inspector should resolve the first deterministic profile.");
        AssertEx.NotNull(projectionB, "Runtime inspector should resolve the second deterministic profile.");

        AssertEx.SequenceEqual(
            projectionA!.ResolvedRulePacks.Select(static pack => pack.RulePack.Id),
            projectionB!.ResolvedRulePacks.Select(static pack => pack.RulePack.Id),
            "Runtime inspector should sort resolved RulePacks deterministically.");
        AssertEx.SequenceEqual(
            projectionA.ResolvedRulePacks.Select(static pack => string.Join(",", pack.CapabilityIds)),
            projectionB.ResolvedRulePacks.Select(static pack => string.Join(",", pack.CapabilityIds)),
            "Runtime inspector should sort RulePack capability ids deterministically.");
        AssertEx.SequenceEqual(
            projectionA.ProviderBindings.Select(static binding => $"{binding.CapabilityId}|{binding.ProviderId}|{binding.PackId}"),
            projectionB.ProviderBindings.Select(static binding => $"{binding.CapabilityId}|{binding.ProviderId}|{binding.PackId}"),
            "Runtime inspector should sort provider bindings and resolve pack ids deterministically.");
        AssertEx.True(
            projectionA.ProviderBindings.All(static binding => string.Equals(binding.PackId, "house-rules", StringComparison.Ordinal)),
            "Runtime inspector should prefer the longest matching RulePack id when resolving provider provenance.");
        AssertEx.SequenceEqual(
            projectionA.CompatibilityDiagnostics.Select(static diagnostic => diagnostic.MessageParameters![0].Value.StringValue),
            ["missing-alpha", "missing-zeta"],
            "Runtime inspector should emit missing-pack diagnostics in deterministic pack order.");
        AssertEx.SequenceEqual(
            projectionA.MigrationPreview.Select(static item => item.SubjectId),
            ["house", "house-rules", "missing-alpha", "missing-zeta"],
            "Runtime inspector migration preview should follow deterministic RulePack ordering.");
    }

    private static void AiExplainProjectionEmitsStructuredProvenance()
    {
        DefaultAiExplainService service = new(
            new AiDigestServiceStub(),
            new RulesetPluginRegistry([new ExplainTestRulesetPlugin()]));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                RuntimeFingerprint: "sha256:test-runtime",
                CharacterId: "char-1",
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                RulesetId: RulesetDefaults.Sr5));

        AssertEx.NotNull(projection, "Explain service should resolve the seeded explain projection.");
        AssertEx.NotNull(projection!.Provenance, "Explain projections should expose structured provenance.");
        AssertEx.Equal(
            "initiative.total",
            projection.ExplainEntryId,
            "Explain projections should default to the trace target key when the caller omits an explicit explain entry id.");
        AssertEx.Equal(
            AiExplainEntryKinds.DerivedValue,
            projection.Kind,
            "Derived explain projections should not be reclassified as quick actions merely because the descriptor is session-safe.");
        AssertEx.Equal(
            "combat-pack/derive.initiative",
            projection.ProviderId,
            "Explain projections should surface the resolved provider id.");
        AssertEx.Equal(
            "official.sr5.ops",
            projection.Provenance!.ProfileId,
            "Explain provenance should carry the active profile id when a session profile is available.");
        AssertEx.Equal(
            "combat-pack/derive.initiative",
            projection.Provenance.ProviderId,
            "Explain provenance should preserve the resolved provider id.");
        AssertEx.Equal(
            "combat-pack",
            projection.Provenance.PackId,
            "Explain provenance should resolve the bound RulePack id.");
        AssertEx.NotNull(
            projection.ProvenanceEnvelope,
            "Explain projections should emit structured provenance envelopes.");
        AssertEx.Equal(
            AiExplainEnvelopeSchemas.ProvenanceV1,
            projection.ProvenanceEnvelope!.Schema,
            "Explain provenance envelopes should publish the canonical schema marker.");
        AssertEx.NotNull(
            projection.EvidenceEnvelope,
            "Explain projections should emit structured evidence envelopes.");
        AssertEx.Equal(
            AiExplainEnvelopeSchemas.EvidenceV1,
            projection.EvidenceEnvelope!.Schema,
            "Explain evidence envelopes should publish the canonical schema marker.");
        AssertEx.True(
            projection.Evidence is { Count: >= 5 },
            "Explain projections should emit machine-readable evidence pointers.");
        IReadOnlyList<AiExplainEvidencePointerProjection> explainEvidence = projection.Evidence!;
        AiExplainEvidenceEnvelopeProjection evidenceEnvelope = projection.EvidenceEnvelope!;
        AssertEx.True(
            evidenceEnvelope.Pointers.Count == explainEvidence.Count,
            "Explain evidence envelopes should preserve the deterministic evidence pointer set.");
        AssertEx.True(
            explainEvidence.Any(pointer =>
                string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleReference, StringComparison.Ordinal)
                && string.Equals(pointer.Pointer, "sr5.combat.initiative", StringComparison.Ordinal)),
            "Explain projections should surface rule-reference evidence from the underlying trace.");
        AssertEx.True(
            projection.Trace is { Count: >= 2 },
            "Explain projections should emit structured trace steps.");
        IReadOnlyList<AiExplainTraceStepProjection> trace = projection.Trace!;
        AssertEx.True(
            trace.Any(step =>
                string.Equals(step.Category, "derived-value", StringComparison.Ordinal)
                && string.Equals(step.RuleId, "sr5.combat.initiative", StringComparison.Ordinal)
                && step.Evidence is { Count: > 0 }),
            "Explain trace steps should preserve step-level provenance and evidence.");
        AssertEx.True(
            trace.Any(step =>
                string.Equals(step.Category, "diagnostic", StringComparison.Ordinal)
                && string.Equals(step.ExplanationKey, "ruleset.diagnostic.soft-cap", StringComparison.Ordinal)),
            "Explain projections should normalize diagnostics into keyed trace steps.");
    }

    private static void AiExplainProjectionPrefersTraceContextOverMismatchedSessionContext()
    {
        DefaultAiExplainService service = new(
            new AiDigestServiceMismatchedSessionStub(),
            new RulesetPluginRegistry([new ExplainTestRulesetPlugin()]));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                RuntimeFingerprint: "sha256:test-runtime",
                CharacterId: "char-1",
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                RulesetId: RulesetDefaults.Sr5));

        AssertEx.NotNull(projection, "Explain service should still resolve a projection when the runtime summary is valid.");
        AssertEx.Equal(
            "initiative.total",
            projection!.ExplainEntryId,
            "Explain projections should still derive the explain entry id from the trace target.");
        AssertEx.Equal(
            "official.sr5.ops",
            projection.Provenance?.ProfileId,
            "Explain provenance should prefer the structured trace profile when the current session digest points at another runtime.");
        AssertEx.Equal(
            "combat-pack/derive.initiative",
            projection.ProviderId,
            "Explain projections should fall back to trace provider ids when the runtime binding map is incomplete.");
        AssertEx.Equal(
            "combat-pack",
            projection.PackId,
            "Explain projections should fall back to trace pack ids when provider bindings cannot resolve a pack.");
        AssertEx.NotNull(
            projection.ProvenanceEnvelope,
            "Explain projections should preserve provenance envelopes when runtime/session contexts diverge.");
        AssertEx.NotNull(
            projection.EvidenceEnvelope,
            "Explain projections should preserve evidence envelopes when runtime/session contexts diverge.");
        AssertEx.True(
            projection.Evidence is { Count: > 0 }
            && !projection.Evidence.Any(pointer =>
                string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleProfile, StringComparison.Ordinal)
                && string.Equals(pointer.Pointer, "wrong-profile", StringComparison.Ordinal)),
            "Explain evidence should not leak session profile pointers from a mismatched runtime.");
    }

    private static void ContractGoldenJsonFixturesStayStable()
    {
        AssertGoldenJsonFixture("runtime-summary.golden.json", CreateRuntimeSummaryFixture());
        AssertGoldenJsonFixture("explain-trace.golden.json", CreateExplainTraceFixture());
        AssertGoldenJsonFixture("runtime-lock-diff.golden.json", CreateRuntimeLockDiffFixture());
        AssertGoldenJsonFixture("session-ledger.golden.json", CreateSessionLedgerFixture());
    }

    private static void ContractNormalizationFixturesStayStable()
    {
        RuntimeLockInstallPreviewReceipt normalizedInstallPreviewA =
            RuntimeCompatibilityContractNormalizer.NormalizeRuntimeLockInstallPreview(CreateRuntimeLockInstallPreviewFixtureA());
        RuntimeLockInstallPreviewReceipt normalizedInstallPreviewB =
            RuntimeCompatibilityContractNormalizer.NormalizeRuntimeLockInstallPreview(CreateRuntimeLockInstallPreviewFixtureB());
        BuildKitManifest normalizedBuildKitManifestA =
            RuntimeCompatibilityContractNormalizer.NormalizeBuildKitManifest(CreateBuildKitManifestFixtureA());
        BuildKitManifest normalizedBuildKitManifestB =
            RuntimeCompatibilityContractNormalizer.NormalizeBuildKitManifest(CreateBuildKitManifestFixtureB());
        RuntimeLockInstallCandidate normalizedInstallCandidateA =
            RuntimeCompatibilityContractNormalizer.NormalizeRuntimeLockInstallCandidate(CreateRuntimeLockInstallCandidateFixtureA());
        RuntimeLockInstallCandidate normalizedInstallCandidateB =
            RuntimeCompatibilityContractNormalizer.NormalizeRuntimeLockInstallCandidate(CreateRuntimeLockInstallCandidateFixtureB());

        AssertEx.Equal(
            JsonSerializer.Serialize(normalizedInstallPreviewA, GoldenJsonOptions),
            JsonSerializer.Serialize(normalizedInstallPreviewB, GoldenJsonOptions),
            "Runtime-lock install previews should normalize order-insensitive inputs into a deterministic payload shape.");
        AssertEx.Equal(
            JsonSerializer.Serialize(normalizedBuildKitManifestA, GoldenJsonOptions),
            JsonSerializer.Serialize(normalizedBuildKitManifestB, GoldenJsonOptions),
            "BuildKit manifests should normalize order-insensitive inputs into a deterministic payload shape.");
        AssertEx.Equal(
            JsonSerializer.Serialize(normalizedInstallCandidateA, GoldenJsonOptions),
            JsonSerializer.Serialize(normalizedInstallCandidateB, GoldenJsonOptions),
            "Runtime compatibility candidates should normalize diagnostics into a deterministic payload shape.");

        AssertGoldenJsonFixture("runtime-lock-install-preview.normalized.golden.json", normalizedInstallPreviewA);
        AssertGoldenJsonFixture("buildkit-manifest.normalized.golden.json", normalizedBuildKitManifestA);
        AssertGoldenJsonFixture("runtime-lock-install-candidate.normalized.golden.json", normalizedInstallCandidateA);
    }

    private static void SessionAndRuntimeCompatibilityProjectionsStayDeterministic()
    {
        CharacterVersionReference baseCharacterVersion = new("char-1", "ver-1", RulesetDefaults.Sr5, "sha256:test");
        SessionEventEnvelope olderEvent = new(
            EventId: "evt-older",
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            DeviceId: "device-1",
            ActorId: "actor-1",
            Sequence: 7,
            EventType: SessionOverlayEventKinds.PinChanged,
            Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["actionId"] = RulesetCapabilityBridge.FromObject("quick-heal"),
                ["isPinned"] = RulesetCapabilityBridge.FromObject(false)
            },
            CreatedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(1));
        SessionEventEnvelope newerEvent = new(
            EventId: "evt-newer",
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            DeviceId: "device-1",
            ActorId: "actor-1",
            Sequence: 7,
            EventType: SessionOverlayEventKinds.PinChanged,
            Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["actionId"] = RulesetCapabilityBridge.FromObject("quick-heal"),
                ["isPinned"] = RulesetCapabilityBridge.FromObject(true)
            },
            CreatedAtUtc: DateTimeOffset.UnixEpoch.AddSeconds(2));

        DefaultSessionOverlayProjectionService sessionService = new();
        SessionOverlayProjection projectionA = sessionService.Replay(
            overlayId: "overlay-1",
            characterId: "char-1",
            runtimeFingerprint: "sha256:test",
            events: [newerEvent, olderEvent]);
        SessionOverlayProjection projectionB = sessionService.Replay(
            overlayId: "overlay-1",
            characterId: "char-1",
            runtimeFingerprint: "sha256:test",
            events: [olderEvent, newerEvent]);

        AssertEx.SequenceEqual(
            projectionA.AppliedEvents.Select(static candidate => candidate.EventId),
            projectionB.AppliedEvents.Select(static candidate => candidate.EventId),
            "Session replay should deterministically order same-sequence envelopes by timestamp and event id.");
        AssertEx.SequenceEqual(
            projectionA.PinnedActionIds,
            projectionB.PinnedActionIds,
            "Session replay should project stable compatibility state regardless of caller event ordering.");
        AssertEx.SequenceEqual(
            projectionA.PinnedActionIds,
            ["quick-heal"],
            "Session replay should resolve same-sequence pin changes consistently.");

        DefaultHubProjectCompatibilityService compatibilityService = new(
            new RulesetPluginRegistry([new Sr5RulesetPlugin()]),
            new RulePackRegistryServiceStub(
            [
                CreateRulePackEntry(
                    packId: "house-rules",
                    capabilities:
                    [
                        new RulePackCapabilityDescriptor(RulePackCapabilityIds.DeriveStat, RulePackAssetKinds.Lua, RulePackAssetModes.AddProvider)
                    ]),
                CreateRulePackEntry(
                    packId: "combat-pack",
                    capabilities:
                    [
                        new RulePackCapabilityDescriptor(RulePackCapabilityIds.SessionQuickActions, RulePackAssetKinds.Lua, RulePackAssetModes.AddProvider, SessionSafe: true)
                    ])
            ]),
            new RuleProfileRegistryServiceStub([CreateProfile()]),
            new BuildKitRegistryServiceStub(
            [
                CreateBuildKitRegistryEntry("buildkit-a", CreateBuildKitManifestFixtureA()),
                CreateBuildKitRegistryEntry("buildkit-b", CreateBuildKitManifestFixtureB())
            ]),
            new RuntimeLockRegistryServiceStub(
            [
                CreateRuntimeLockRegistryEntry(
                    lockId: "runtime-a",
                    runtimeLock: CreateCompatibilityRuntimeLockFixtureA()),
                CreateRuntimeLockRegistryEntry(
                    lockId: "runtime-b",
                    runtimeLock: CreateCompatibilityRuntimeLockFixtureB() with { RulesetId = RulesetDefaults.Sr5 })
            ]));

        HubProjectCompatibilityMatrix? runtimeMatrixA = compatibilityService.GetMatrix(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.RuntimeLock,
            "runtime-a",
            RulesetDefaults.Sr5);
        HubProjectCompatibilityMatrix? runtimeMatrixB = compatibilityService.GetMatrix(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.RuntimeLock,
            "runtime-b",
            RulesetDefaults.Sr5);
        AssertEx.NotNull(runtimeMatrixA, "Runtime compatibility matrix should resolve for runtime-a.");
        AssertEx.NotNull(runtimeMatrixB, "Runtime compatibility matrix should resolve for runtime-b.");

        AssertEx.SequenceEqual(
            runtimeMatrixA!.Rows.Select(FormatCompatibilityRow),
            runtimeMatrixB!.Rows.Select(FormatCompatibilityRow),
            "Runtime compatibility projections should remain deterministic across runtime lock ordering variance.");
        AssertEx.SequenceEqual(
            runtimeMatrixA.Capabilities?.Select(FormatCapabilityProjection) ?? [],
            runtimeMatrixB.Capabilities?.Select(FormatCapabilityProjection) ?? [],
            "Runtime compatibility capability projections should remain deterministic across runtime lock ordering variance.");

        HubProjectCompatibilityMatrix? buildKitMatrixA = compatibilityService.GetMatrix(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.BuildKit,
            "buildkit-a",
            RulesetDefaults.Sr5);
        HubProjectCompatibilityMatrix? buildKitMatrixB = compatibilityService.GetMatrix(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.BuildKit,
            "buildkit-b",
            RulesetDefaults.Sr5);
        AssertEx.NotNull(buildKitMatrixA, "BuildKit compatibility matrix should resolve for buildkit-a.");
        AssertEx.NotNull(buildKitMatrixB, "BuildKit compatibility matrix should resolve for buildkit-b.");

        AssertEx.SequenceEqual(
            buildKitMatrixA!.Rows.Select(FormatCompatibilityRow),
            buildKitMatrixB!.Rows.Select(FormatCompatibilityRow),
            "BuildKit compatibility projections should remain deterministic across runtime-requirement ordering variance.");
    }

    private static void LocalizationFallbackHelpersNormalizeLegacyContracts()
    {
        RulesetExplainParameter[] parameters =
        [
            new("profileId", RulesetCapabilityBridge.FromObject("official.sr5.core"))
        ];

        RulesetCapabilityDiagnostic capabilityDiagnostic = new(
            Code: "experimental",
            Message: "ruleset.capability.experimental",
            MessageParameters: parameters);
        AssertEx.Equal(
            "ruleset.capability.experimental",
            RulesetCapabilityDiagnosticLocalization.ResolveMessageKey(capabilityDiagnostic),
            "Capability diagnostics should fall back to their message when a key is omitted.");
        AssertEx.Equal(
            1,
            RulesetCapabilityDiagnosticLocalization.ResolveMessageParameters(capabilityDiagnostic).Count,
            "Capability diagnostics should expose deterministic parameter collections.");

        RuntimeLockCompatibilityDiagnostic compatibilityDiagnostic = new(
            State: RuntimeLockCompatibilityStates.Compatible,
            Message: "runtime.lock.compatibility.compatible",
            RequiredRulesetId: RulesetDefaults.Sr5,
            RequiredRuntimeFingerprint: "sha256:runtime");
        AssertEx.Equal(
            "runtime.lock.compatibility.compatible",
            RuntimeLockContractLocalization.ResolveCompatibilityMessageKey(compatibilityDiagnostic),
            "Runtime lock compatibility diagnostics should fall back to their message when a key is omitted.");
        AssertEx.Equal(
            0,
            RuntimeLockContractLocalization.ResolveCompatibilityMessageParameters(compatibilityDiagnostic).Count,
            "Runtime lock compatibility diagnostics should normalize missing parameter lists to empty collections.");

        RuntimeInspectorWarning warning = new(
            Kind: RuntimeInspectorWarningKinds.Trust,
            Severity: RuntimeInspectorWarningSeverityLevels.Info,
            Message: "runtime.inspector.warning.trust.local-only");
        AssertEx.Equal(
            "runtime.inspector.warning.trust.local-only",
            RuntimeInspectorContractLocalization.ResolveMessageKey(warning),
            "Runtime inspector warnings should fall back to their message when a key is omitted.");
        AssertEx.Equal(
            0,
            RuntimeInspectorContractLocalization.ResolveMessageParameters(warning).Count,
            "Runtime inspector warnings should normalize missing parameter lists to empty collections.");

        RuntimeMigrationPreviewItem migrationPreview = new(
            Kind: RuntimeMigrationPreviewChangeKinds.RulePackAdded,
            Summary: "runtime.inspector.preview.rulepack-added");
        AssertEx.Equal(
            "runtime.inspector.preview.rulepack-added",
            RuntimeInspectorContractLocalization.ResolveSummaryKey(migrationPreview),
            "Runtime migration preview items should fall back to their summary when a key is omitted.");
        AssertEx.Equal(
            0,
            RuntimeInspectorContractLocalization.ResolveSummaryParameters(migrationPreview).Count,
            "Runtime migration preview items should normalize missing parameter lists to empty collections.");

        RulePackResolutionDiagnostic resolutionDiagnostic = new(
            Kind: RulePackResolutionDiagnosticKinds.MissingDependency,
            Severity: RulePackResolutionSeverityLevels.Warning,
            SubjectId: "house-rules",
            Message: "rulepack.compile.missing-dependency");
        AssertEx.Equal(
            "rulepack.compile.missing-dependency",
            RulePackResolutionDiagnosticLocalization.ResolveMessageKey(resolutionDiagnostic),
            "RulePack resolution diagnostics should fall back to their message when a key is omitted.");
        AssertEx.Equal(
            0,
            RulePackResolutionDiagnosticLocalization.ResolveMessageParameters(resolutionDiagnostic).Count,
            "RulePack resolution diagnostics should normalize missing parameter lists to empty collections.");

        BuildKitValidationIssue buildKitIssue = new(
            Kind: BuildKitValidationIssueKinds.MissingRulePack,
            Message: "buildkit.validation.missing-rulepack");
        AssertEx.Equal(
            "buildkit.validation.missing-rulepack",
            BuildKitContractLocalization.ResolveIssueMessageKey(buildKitIssue),
            "Build kit validation issues should fall back to their message when a key is omitted.");
        AssertEx.Equal(
            0,
            BuildKitContractLocalization.ResolveIssueMessageParameters(buildKitIssue).Count,
            "Build kit validation issues should normalize missing parameter lists to empty collections.");

        RuntimeLockInstallPreviewItem runtimeLockPreview = new(
            Kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
            Summary: "runtime.lock.install.preview.runtime-lock-pinned",
            SubjectId: "sha256:runtime");
        AssertEx.Equal(
            "runtime.lock.install.preview.runtime-lock-pinned",
            RuntimeLockContractLocalization.ResolveInstallPreviewSummaryKey(runtimeLockPreview),
            "Runtime lock install previews should fall back to their summary when a key is omitted.");
        AssertEx.Equal(
            0,
            RuntimeLockContractLocalization.ResolveInstallPreviewSummaryParameters(runtimeLockPreview).Count,
            "Runtime lock install previews should normalize missing parameter lists to empty collections.");

        RulePackInstallPreviewItem rulePackPreview = new(
            Kind: RulePackInstallPreviewChangeKinds.InstallStateChanged,
            Summary: "rulepack.install.preview.install-state-changed",
            SubjectId: "house-rules");
        AssertEx.Equal(
            "rulepack.install.preview.install-state-changed",
            RulePackInstallContractLocalization.ResolvePreviewSummaryKey(rulePackPreview),
            "RulePack install previews should fall back to their summary when a key is omitted.");
        AssertEx.Equal(
            0,
            RulePackInstallContractLocalization.ResolvePreviewSummaryParameters(rulePackPreview).Count,
            "RulePack install previews should normalize missing parameter lists to empty collections.");

        RuleProfilePreviewItem ruleProfilePreview = new(
            Kind: RuleProfilePreviewChangeKinds.RuntimeLockPinned,
            Summary: "ruleprofile.preview.runtime-lock-pinned");
        AssertEx.Equal(
            "ruleprofile.preview.runtime-lock-pinned",
            RuleProfileContractLocalization.ResolvePreviewSummaryKey(ruleProfilePreview),
            "Rule profile previews should fall back to their summary when a key is omitted.");
        AssertEx.Equal(
            0,
            RuleProfileContractLocalization.ResolvePreviewSummaryParameters(ruleProfilePreview).Count,
            "Rule profile previews should normalize missing parameter lists to empty collections.");

        HubProjectCompatibilityRow compatibilityRow = new(
            Kind: HubProjectCompatibilityRowKinds.SessionRuntime,
            Label: "Session Runtime Bundle",
            State: HubProjectCompatibilityStates.Compatible,
            CurrentValue: "bundle-ready",
            RequiredValue: RulePackExecutionEnvironments.SessionRuntimeBundle,
            Notes: "2 RulePack(s) resolved");
        AssertEx.Equal(
            "Session Runtime Bundle",
            HubProjectCompatibilityContractLocalization.ResolveLabelKey(compatibilityRow),
            "Hub project compatibility rows should fall back to their label when a key is omitted.");
        AssertEx.Equal(
            "bundle-ready",
            HubProjectCompatibilityContractLocalization.ResolveCurrentValueKey(compatibilityRow),
            "Hub project compatibility rows should fall back to their current value when a key is omitted.");
        AssertEx.Equal(
            RulePackExecutionEnvironments.SessionRuntimeBundle,
            HubProjectCompatibilityContractLocalization.ResolveRequiredValueKey(compatibilityRow),
            "Hub project compatibility rows should fall back to their required value when a key is omitted.");
        AssertEx.Equal(
            "2 RulePack(s) resolved",
            HubProjectCompatibilityContractLocalization.ResolveNotesKey(compatibilityRow),
            "Hub project compatibility rows should fall back to their notes when a key is omitted.");
        AssertEx.Equal(
            0,
            HubProjectCompatibilityContractLocalization.ResolveNotesParameters(compatibilityRow).Count,
            "Hub project compatibility rows should normalize missing parameter lists to empty collections.");
    }

    private static void JournalProjectionIsDeterministicAndValidated()
    {
        DefaultJournalProjectionService service = new();
        JournalProjection projection = service.BuildProjection(
            scopeKind: JournalScopeKinds.Session,
            scopeId: "session-7",
            notes:
            [
                new NoteDocument(
                    NoteId: "note-2",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Title: "Aftermath",
                    Blocks:
                    [
                        new NoteBlock("block-b", NoteBlockKinds.Paragraph, "Second", DateTimeOffset.UnixEpoch.AddHours(2)),
                        new NoteBlock("block-a", NoteBlockKinds.Paragraph, "First", DateTimeOffset.UnixEpoch.AddHours(1))
                    ],
                    UpdatedAtUtc: DateTimeOffset.UnixEpoch.AddHours(4)),
                new NoteDocument(
                    NoteId: "note-1",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Title: "Prep",
                    Blocks:
                    [
                        new NoteBlock("block-c", NoteBlockKinds.Paragraph, "Only", DateTimeOffset.UnixEpoch.AddHours(3))
                    ],
                    UpdatedAtUtc: DateTimeOffset.UnixEpoch.AddHours(2))
            ],
            ledgerEntries:
            [
                new LedgerEntry(
                    EntryId: "ledger-2",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Kind: LedgerEntryKinds.Expense,
                    Amount: -200m,
                    Currency: "nuyen",
                    Label: "Bribes",
                    OccurredAtUtc: DateTimeOffset.UnixEpoch.AddHours(4),
                    NoteId: "missing-note"),
                new LedgerEntry(
                    EntryId: "ledger-1",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Kind: LedgerEntryKinds.Karma,
                    Amount: 2m,
                    Currency: "karma",
                    Label: "Run reward",
                    OccurredAtUtc: DateTimeOffset.UnixEpoch.AddHours(1),
                    NoteId: "note-1")
            ],
            timelineEvents:
            [
                new TimelineEvent(
                    EventId: "timeline-2",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Kind: TimelineEventKinds.Note,
                    Title: "Cleanup",
                    StartsAtUtc: DateTimeOffset.UnixEpoch.AddHours(6),
                    EndsAtUtc: DateTimeOffset.UnixEpoch.AddHours(5),
                    NoteId: "note-2",
                    LedgerEntryId: "missing-ledger"),
                new TimelineEvent(
                    EventId: "timeline-1",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: JournalScopeKinds.Session,
                    ScopeId: "session-7",
                    Kind: TimelineEventKinds.Session,
                    Title: "Meet",
                    StartsAtUtc: DateTimeOffset.UnixEpoch.AddHours(2),
                    EndsAtUtc: DateTimeOffset.UnixEpoch.AddHours(3),
                    NoteId: "note-1",
                    LedgerEntryId: "ledger-1")
            ]);

        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics = service.Validate(projection);

        AssertEx.SequenceEqual(
            projection.Notes.Select(static note => note.NoteId),
            ["note-1", "note-2"],
            "Journal projections should order notes deterministically.");
        AssertEx.SequenceEqual(
            projection.Notes[1].Blocks.Select(static block => block.BlockId),
            ["block-a", "block-b"],
            "Journal projections should order note blocks deterministically.");
        AssertEx.SequenceEqual(
            projection.LedgerEntries.Select(static entry => entry.EntryId),
            ["ledger-1", "ledger-2"],
            "Journal projections should order ledger entries deterministically.");
        AssertEx.SequenceEqual(
            projection.TimelineEvents.Select(static entry => entry.EventId),
            ["timeline-1", "timeline-2"],
            "Journal projections should order timeline events deterministically.");
        AssertEx.SequenceEqual(
            diagnostics.Select(static diagnostic => diagnostic.Code),
            ["journal.ledger.note-missing", "journal.timeline.invalid-range", "journal.timeline.ledger-missing"],
            "Journal validation should emit deterministic keyed diagnostics.");
        AssertEx.True(
            diagnostics.All(static diagnostic => string.Equals(diagnostic.MessageKey, diagnostic.Code, StringComparison.Ordinal)),
            "Journal validation should keep diagnostics localization-ready.");
    }

    private static void BuildLabOutputsAreDeterministicAndLocalized()
    {
        DefaultBuildLabService service = new();

        IReadOnlyList<BuildVariantProjection> variantsA = service.GenerateBuildVariants(
            "Alice Runner",
            ["street-samurai", "face", "face"]);
        IReadOnlyList<BuildVariantProjection> variantsB = service.GenerateBuildVariants(
            "Alice Runner",
            ["face", "street-samurai"]);
        IReadOnlyList<BuildVariantProjection> fallbackVariants = service.GenerateBuildVariants("Alice Runner", []);
        KarmaSpendProjection progression = service.ProjectKarmaSpend("Alice Runner", string.Empty, []);
        IReadOnlyList<BuildTrapChoice> traps = service.DetectTrapChoices("Alice Runner", "alice-runner-face-1");
        IReadOnlyList<BuildRoleOverlap> overlaps = service.DetectRoleOverlap(
            "Alice Runner",
            ["alice-runner-face-2", "alice-runner-generalist-1", "alice-runner-face-1"]);
        IReadOnlyList<BuildCorePackageSuggestion> packages = service.SuggestCorePackages("Alice Runner", "alice-runner-face-1");
        BuildVariantProjection? scoredVariant = service.ScoreBuildVariant("Alice Runner", "alice-runner-face-1");

        AssertEx.SequenceEqual(
            variantsA.Select(static variant => variant.VariantId),
            variantsB.Select(static variant => variant.VariantId),
            "Build Lab variants should remain deterministic across tag ordering and duplicate inputs.");
        AssertEx.True(
            variantsA.All(static variant =>
                string.Equals(variant.LabelKey, "buildlab.variant.label", StringComparison.Ordinal)
                && string.Equals(variant.SummaryKey, "buildlab.variant.summary", StringComparison.Ordinal)
                && variant.LabelParameters.Count > 0
                && variant.SummaryParameters.Count > 0
                && !string.IsNullOrWhiteSpace(variant.ExplainEntryId)),
            "Build Lab variants should expose localization-ready keys, parameters, and explain entry ids.");
        AssertEx.Equal(
            "buildlab.variant.constraint.secondary-role",
            variantsA[1].Constraints[0].ConstraintKey,
            "Secondary Build Lab variants should emit keyed constraints.");
        AssertEx.Equal(
            "buildlab.variant.role-tag-defaulted",
            fallbackVariants[0].Diagnostics![0].MessageKey,
            "Build Lab should emit keyed diagnostics when role tags fall back to the generalist variant.");

        AssertEx.SequenceEqual(
            progression.Steps.Select(static step => step.KarmaTotal),
            [25, 50, 100],
            "Build Lab progression should default milestones deterministically.");
        AssertEx.Equal(
            "buildlab.progression.milestone-defaulted",
            progression.Diagnostics![0].MessageKey,
            "Build Lab progression should emit keyed diagnostics when milestones default.");
        AssertEx.True(
            progression.Steps.All(static step =>
                string.Equals(step.SummaryKey, "buildlab.progression.step.summary", StringComparison.Ordinal)
                && step.SummaryParameters.Count > 0
                && !string.IsNullOrWhiteSpace(step.ExplainEntryId)),
            "Build Lab progression steps should expose localization-ready summaries.");

        AssertEx.Equal(
            "buildlab.trap.resource-overcommit",
            traps[0].ReasonKey,
            "Build Lab trap choices should emit keyed reasons.");
        AssertEx.True(
            traps[0].Parameters.Count >= 4 && !string.IsNullOrWhiteSpace(traps[0].ExplainEntryId),
            "Build Lab trap choices should expose structured parameters and explain entry ids.");

        AssertEx.SequenceEqual(
            overlaps.Select(static overlap => $"{overlap.LeftVariantId}|{overlap.RightVariantId}|{overlap.OverlapScore}"),
            [
                "alice-runner-face-1|alice-runner-face-2|1.0",
                "alice-runner-face-1|alice-runner-generalist-1|0.6",
                "alice-runner-face-2|alice-runner-generalist-1|0.6"
            ],
            "Build Lab role-overlap projections should remain deterministically ranked.");
        AssertEx.True(
            overlaps.All(static overlap =>
                string.Equals(overlap.ReasonKey, "buildlab.role-overlap.summary", StringComparison.Ordinal)
                && overlap.ReasonParameters.Count == 4
                && !string.IsNullOrWhiteSpace(overlap.ExplainEntryId)),
            "Build Lab role-overlap projections should expose keyed reasons and explain hooks.");

        AssertEx.SequenceEqual(
            packages.Select(static package => package.PackageId),
            ["face.core.a", "face.core.b"],
            "Build Lab package suggestions should remain deterministically ranked.");
        AssertEx.True(
            packages.All(static package =>
                string.Equals(package.LabelKey, "buildlab.package.label", StringComparison.Ordinal)
                && string.Equals(package.SummaryKey, "buildlab.package.summary", StringComparison.Ordinal)
                && package.LabelParameters.Count > 0
                && package.SummaryParameters.Count > 0
                && !string.IsNullOrWhiteSpace(package.ExplainEntryId)),
            "Build Lab package suggestions should expose localization-ready labels, summaries, and explain hooks.");

        AssertEx.NotNull(scoredVariant, "Build Lab should resolve exact variant ids when scoring a generated variant.");
    }

    private static void ValidationSummaryAndExplainHookCompositionStayDeterministic()
    {
        DefaultExplainHookComposer explainHookComposer = new();
        DefaultValidationSummaryService validationSummaryService = new();

        ExplainHookReference ledgerExplain = explainHookComposer.CreateReference(
            targetKind: "ledger-entry",
            targetId: "ledger-2",
            traceId: "trace-ledger-2",
            subjectId: "ledger-2",
            capabilityId: "validate.choice",
            providerId: "provider.alpha",
            packId: "pack.alpha",
            runtimeFingerprint: "sha256:runtime-1");
        ExplainHookReference timelineExplain = explainHookComposer.CreateReference(
            targetKind: "timeline-event",
            targetId: "timeline-2",
            traceId: "trace-timeline-2",
            subjectId: "timeline-2",
            capabilityId: "validate.choice",
            providerId: "provider.alpha",
            packId: "pack.alpha",
            runtimeFingerprint: "sha256:runtime-1");
        ExplainHookComposition composition = explainHookComposer.Compose(
            compositionId: "validation-run",
            attachments:
            [
                new ExplainHookAttachment("timeline-event", "timeline-2", timelineExplain),
                new ExplainHookAttachment("ledger-entry", "ledger-2", ledgerExplain),
                new ExplainHookAttachment("timeline-event", "timeline-2", timelineExplain)
            ]);

        ValidationSummary summary = validationSummaryService.BuildSummary(
            scopeKind: "Session",
            scopeId: "session-7",
            diagnostics:
            [
                new RulesetCapabilityDiagnostic(
                    Code: "journal.timeline.ledger-missing",
                    Message: "journal.timeline.ledger-missing",
                    Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                    MessageKey: "journal.timeline.ledger-missing",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("subjectId", RulesetCapabilityBridge.FromObject("timeline-2")),
                        new RulesetExplainParameter("providerId", RulesetCapabilityBridge.FromObject("provider.alpha")),
                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("pack.alpha"))
                    ]),
                new RulesetCapabilityDiagnostic(
                    Code: "journal.timeline.invalid-range",
                    Message: "journal.timeline.invalid-range",
                    Severity: RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "journal.timeline.invalid-range",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("subjectId", RulesetCapabilityBridge.FromObject("timeline-2")),
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject("validate.choice"))
                    ]),
                new RulesetCapabilityDiagnostic(
                    Code: "journal.ledger.note-missing",
                    Message: "journal.ledger.note-missing",
                    Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                    MessageKey: "journal.ledger.note-missing",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("subjectId", RulesetCapabilityBridge.FromObject("ledger-2")),
                        new RulesetExplainParameter("providerId", RulesetCapabilityBridge.FromObject("provider.alpha")),
                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("pack.alpha"))
                    ])
            ],
            runtimeFingerprint: "sha256:runtime-1",
            explainHooksByCode: new Dictionary<string, ExplainHookReference>(StringComparer.Ordinal)
            {
                ["journal.ledger.note-missing"] = ledgerExplain,
                ["journal.timeline.ledger-missing"] = timelineExplain
            });

        AssertEx.SequenceEqual(
            composition.Attachments.Select(static entry => $"{entry.TargetKind}:{entry.TargetId}:{entry.Explain.HookId}"),
            [
                "ledger-entry:ledger-2:ledger-entry:ledger-2:trace-ledger-2",
                "timeline-event:timeline-2:timeline-event:timeline-2:trace-timeline-2"
            ],
            "Explain-hook composition should deduplicate and sort deterministic attachment keys.");
        AssertEx.Equal(
            ValidationSummaryStates.Invalid,
            summary.State,
            "Validation summaries should become invalid when at least one error diagnostic exists.");
        AssertEx.Equal(
            "validation.summary.invalid",
            ValidationSummaryLocalization.ResolveSummaryKey(summary),
            "Validation summaries should expose localization-safe summary keys.");
        AssertEx.SequenceEqual(
            summary.Failures.Select(static failure => failure.Code),
            ["journal.timeline.invalid-range", "journal.ledger.note-missing", "journal.timeline.ledger-missing"],
            "Validation summary failures should sort deterministically by severity then code.");
        AssertEx.True(
            summary.Failures.All(static failure => string.Equals(failure.RuntimeFingerprint, "sha256:runtime-1", StringComparison.Ordinal)),
            "Validation summary failures should carry normalized runtime fingerprint context.");
        AssertEx.True(
            string.Equals(summary.Failures[1].Explain?.HookId, "ledger-entry:ledger-2:trace-ledger-2", StringComparison.Ordinal)
            && string.Equals(summary.Failures[2].Explain?.HookId, "timeline-event:timeline-2:trace-timeline-2", StringComparison.Ordinal),
            "Validation summaries should attach explain-hook references for downstream integration surfaces.");
    }

    private static void ContentInstallPreviewsEmitLocalizationKeys()
    {
        RuleProfileApplyTarget sessionLedgerTarget = new(RuleProfileApplyTargetKinds.SessionLedger, "session-1");
        RuleProfileApplyTarget workspaceTarget = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        RuntimeLockRegistryEntry runtimeLockEntry = new(
            LockId: "runtime-lock-1",
            Owner: OwnerScope.LocalSingleUser,
            Title: "Seattle Runtime",
            Visibility: ArtifactVisibilityModes.LocalOnly,
            CatalogKind: RuntimeLockCatalogKinds.Saved,
            RuntimeLock: new ResolvedRuntimeLock(
                RulesetId: RulesetDefaults.Sr5,
                ContentBundles:
                [
                    new ContentBundleDescriptor(
                        BundleId: "official.sr5.base",
                        RulesetId: RulesetDefaults.Sr5,
                        Version: "schema-1",
                        Title: "SR5 Base",
                        Description: "Built-in base content.",
                        AssetPaths: ["data/", "lang/"])
                ],
                RulePacks: [],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                EngineApiVersion: "rulepack-v1",
                RuntimeFingerprint: "sha256:runtime-lock-1"),
            UpdatedAtUtc: DateTimeOffset.UnixEpoch.AddHours(1),
            Description: "Pinned local runtime.",
            Install: new ArtifactInstallState(ArtifactInstallStates.Available));
        DefaultRuntimeLockInstallService runtimeLockService = new(
            new RuntimeLockRegistryServiceStub([runtimeLockEntry]),
            new RuntimeLockInstallHistoryStoreStub());
        RuntimeLockInstallPreviewReceipt? runtimeLockPreview = runtimeLockService.Preview(
            OwnerScope.LocalSingleUser,
            runtimeLockEntry.LockId,
            sessionLedgerTarget,
            RulesetDefaults.Sr5);

        AssertEx.NotNull(runtimeLockPreview, "Runtime-lock preview should resolve the seeded entry.");
        AssertEx.SequenceEqual(
            runtimeLockPreview!.Changes.Select(change => change.SummaryKey ?? string.Empty),
            [
                "runtime.lock.install.preview.runtime-lock-pinned",
                "runtime.lock.install.preview.session-replay-required"
            ],
            "Runtime-lock install previews should expose deterministic keyed summaries.");
        AssertEx.SequenceEqual(
            runtimeLockPreview.Warnings.Select(warning => warning.MessageKey ?? string.Empty),
            [
                "runtime.lock.install.warning.builtin-only",
                "runtime.lock.install.warning.local-only"
            ],
            "Runtime-lock install previews should expose deterministic keyed warnings.");
        AssertEx.True(
            runtimeLockPreview.Changes.All(change =>
                string.Equals(change.Summary, change.SummaryKey, StringComparison.Ordinal)
                && change.SummaryParameters is { Count: > 0 }),
            "Runtime-lock install previews should keep summary payloads localization-ready.");
        AssertEx.True(
            runtimeLockPreview.Warnings.All(warning =>
                string.Equals(warning.Message, warning.MessageKey, StringComparison.Ordinal)
                && warning.MessageParameters is { Count: > 0 }),
            "Runtime-lock install warnings should keep message payloads localization-ready.");

        RulePackRegistryEntry reviewPackEntry = CreateRulePackEntry(
            packId: "review-pack",
            capabilities:
            [
                new RulePackCapabilityDescriptor(
                    RulePackCapabilityIds.ValidateCharacter,
                    RulePackAssetKinds.Lua,
                    RulePackAssetModes.AddProvider,
                    Explainable: true,
                    SessionSafe: false)
            ]);
        RulePackRegistryEntry contentOnlyPackEntry = CreateRulePackEntry(
            packId: "content-only-pack",
            capabilities: []);
        DefaultRulePackInstallService rulePackService = new(
            new RulePackRegistryServiceStub([reviewPackEntry, contentOnlyPackEntry]),
            new RulePackInstallStateStoreStub(),
            new RulePackInstallHistoryStoreStub());
        RulePackInstallPreviewReceipt? reviewPackPreview = rulePackService.Preview(
            OwnerScope.LocalSingleUser,
            "review-pack",
            sessionLedgerTarget,
            RulesetDefaults.Sr5);
        RulePackInstallPreviewReceipt? contentOnlyPackPreview = rulePackService.Preview(
            OwnerScope.LocalSingleUser,
            "content-only-pack",
            workspaceTarget,
            RulesetDefaults.Sr5);

        AssertEx.NotNull(reviewPackPreview, "RulePack preview should resolve the seeded review-required pack.");
        AssertEx.NotNull(contentOnlyPackPreview, "RulePack preview should resolve the seeded content-only pack.");
        AssertEx.SequenceEqual(
            reviewPackPreview!.Changes.Select(change => change.SummaryKey ?? string.Empty),
            [
                "rulepack.install.preview.install-state-changed",
                "rulepack.install.preview.runtime-review-required",
                "rulepack.install.preview.session-replay-required"
            ],
            "RulePack install previews should expose deterministic keyed summaries.");
        AssertEx.SequenceEqual(
            reviewPackPreview.Warnings.Select(warning => warning.MessageKey ?? string.Empty),
            ["rulepack.install.warning.local-only"],
            "RulePack install previews should preserve keyed local-only warnings.");
        AssertEx.Equal(
            "rulepack.install.warning.content-only",
            contentOnlyPackPreview!.Warnings[1].MessageKey,
            "Content-only RulePacks should emit keyed content-only warnings.");
        AssertEx.True(
            reviewPackPreview.Changes.All(change =>
                string.Equals(change.Summary, change.SummaryKey, StringComparison.Ordinal)
                && change.SummaryParameters is { Count: > 0 }),
            "RulePack install previews should keep summary payloads localization-ready.");

        DefaultRuleProfileApplicationService ruleProfileService = new(
            new RuleProfileRegistryServiceStub([CreateProfile(), CreateBuiltinOnlyProfile()]),
            new RuntimeLockInstallServiceStub(),
            new RuleProfileInstallStateStoreStub(),
            new RuleProfileInstallHistoryStoreStub());
        RuleProfilePreviewReceipt? profilePreview = ruleProfileService.Preview(
            OwnerScope.LocalSingleUser,
            "official.sr5.core",
            sessionLedgerTarget,
            RulesetDefaults.Sr5);
        RuleProfilePreviewReceipt? builtinProfilePreview = ruleProfileService.Preview(
            OwnerScope.LocalSingleUser,
            "official.sr5.base-only",
            workspaceTarget,
            RulesetDefaults.Sr5);

        AssertEx.NotNull(profilePreview, "RuleProfile preview should resolve the seeded profile.");
        AssertEx.NotNull(builtinProfilePreview, "RuleProfile preview should resolve the built-in-only profile.");
        AssertEx.SequenceEqual(
            profilePreview!.Changes.Select(change => change.SummaryKey ?? string.Empty),
            [
                "ruleprofile.preview.runtime-lock-pinned",
                "ruleprofile.preview.rulepack-selection-changed",
                "ruleprofile.preview.session-replay-required"
            ],
            "RuleProfile previews should expose deterministic keyed summaries.");
        AssertEx.SequenceEqual(
            profilePreview.Warnings.Select(warning => warning.MessageKey ?? string.Empty),
            ["ruleprofile.preview.warning.local-only"],
            "RuleProfile previews should preserve keyed local-only warnings.");
        AssertEx.Equal(
            "ruleprofile.preview.warning.builtin-only",
            builtinProfilePreview!.Warnings[1].MessageKey,
            "Built-in-only RuleProfiles should emit keyed built-in runtime warnings.");
        AssertEx.True(
            profilePreview.Changes.All(change =>
                string.Equals(change.Summary, change.SummaryKey, StringComparison.Ordinal)
                && change.SummaryParameters is { Count: > 0 }),
            "RuleProfile previews should keep summary payloads localization-ready.");
    }

    private static RuleProfileRegistryEntry CreateProfile()
    {
        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: "official.sr5.core",
                Title: "Official SR5 Core",
                Description: "Curated runtime.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks:
                [
                    new RuleProfilePackSelection(
                        new ArtifactVersionReference("house-rules", "1.0.0"),
                        Required: true,
                        EnabledByDefault: true)
                ],
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: RulesetDefaults.Sr5,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: "official.sr5.base",
                            RulesetId: RulesetDefaults.Sr5,
                            Version: "schema-1",
                            Title: "SR5 Base",
                            Description: "Built-in base content.",
                            AssetPaths: ["data/", "lang/"])
                    ],
                    RulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")],
                    ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RulePackCapabilityIds.ContentCatalog] = "house-rules/content.catalog"
                    },
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: "runtime-lock-sha256"),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available),
            RegistryEntrySourceKinds.BuiltInCoreProfile);
    }

    private static RuleProfileRegistryEntry CreateBuiltinOnlyProfile()
    {
        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: "official.sr5.base-only",
                Title: "Official SR5 Base Only",
                Description: "Base runtime without pack overlays.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks: [],
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: RulesetDefaults.Sr5,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: "official.sr5.base",
                            RulesetId: RulesetDefaults.Sr5,
                            Version: "schema-1",
                            Title: "SR5 Base",
                            Description: "Built-in base content.",
                            AssetPaths: ["data/", "lang/"])
                    ],
                    RulePacks: [],
                    ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: "runtime-lock-sha256-base-only"),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available),
            RegistryEntrySourceKinds.BuiltInCoreProfile);
    }

    private static RuleProfileRegistryEntry CreateDeterministicInspectorProfile(
        string profileId,
        IReadOnlyList<RuleProfilePackSelection> rulePacks,
        IReadOnlyDictionary<string, string> providerBindings)
    {
        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: profileId,
                Title: $"{profileId} Title",
                Description: "Deterministic runtime inspector profile.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks: rulePacks,
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: RulesetDefaults.Sr5,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: "official.sr5.base",
                            RulesetId: RulesetDefaults.Sr5,
                            Version: "schema-1",
                            Title: "SR5 Base",
                            Description: "Built-in base content.",
                            AssetPaths: ["lang/", "data/"])
                    ],
                    RulePacks: rulePacks.Select(static pack => pack.RulePack).ToArray(),
                    ProviderBindings: providerBindings,
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: $"sha256:{profileId}"),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available),
            RegistryEntrySourceKinds.BuiltInCoreProfile);
    }

    private static RulePackRegistryEntry CreateRulePackEntry(
        string packId,
        IReadOnlyList<RulePackCapabilityDescriptor> capabilities)
    {
        return new RulePackRegistryEntry(
            new RulePackManifest(
                PackId: packId,
                Version: "1.0.0",
                Title: $"{packId} Title",
                Author: "GM",
                Description: $"{packId} description.",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "rulepack-v1",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: ArtifactVisibilityModes.LocalOnly,
                TrustTier: ArtifactTrustTiers.LocalOnly,
                Assets: [],
                Capabilities: capabilities,
                ExecutionPolicies: []),
            new RulePackPublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available));
    }

    private static RuntimeLockInstallPreviewReceipt CreateRuntimeLockInstallPreviewFixtureA()
    {
        return new RuntimeLockInstallPreviewReceipt(
            LockId: "runtime-lock-a",
            Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.SessionLedger, "session-a"),
            RuntimeLock: CreateCompatibilityRuntimeLockFixtureA(),
            Changes:
            [
                new RuntimeLockInstallPreviewItem(
                    Kind: RuntimeLockInstallPreviewChangeKinds.SessionReplayRequired,
                    Summary: "runtime.lock.install.preview.session-replay-required",
                    SubjectId: "session-a",
                    RequiresConfirmation: true,
                    SummaryParameters:
                    [
                        new RulesetExplainParameter("targetId", RulesetCapabilityBridge.FromObject("session-a")),
                        new RulesetExplainParameter("targetKind", RulesetCapabilityBridge.FromObject(RuleProfileApplyTargetKinds.SessionLedger))
                    ]),
                new RuntimeLockInstallPreviewItem(
                    Kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
                    Summary: "runtime.lock.install.preview.runtime-lock-pinned",
                    SubjectId: "runtime-lock-a",
                    SummaryParameters:
                    [
                        new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:compat-runtime")),
                        new RulesetExplainParameter("lockId", RulesetCapabilityBridge.FromObject("runtime-lock-a"))
                    ])
            ],
            Warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Trust,
                    Severity: RuntimeInspectorWarningSeverityLevels.Info,
                    Message: "runtime.lock.install.warning.local-only",
                    SubjectId: "runtime-lock-a"),
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.ProviderBinding,
                    Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                    Message: "runtime.lock.install.warning.runtime-review",
                    SubjectId: "runtime-lock-a",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("rulePackCount", RulesetCapabilityBridge.FromObject(2))
                    ]),
            ],
            RequiresConfirmation: false);
    }

    private static RuntimeLockInstallPreviewReceipt CreateRuntimeLockInstallPreviewFixtureB()
    {
        return new RuntimeLockInstallPreviewReceipt(
            LockId: "runtime-lock-a",
            Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.SessionLedger, "session-a"),
            RuntimeLock: CreateCompatibilityRuntimeLockFixtureB(),
            Changes:
            [
                new RuntimeLockInstallPreviewItem(
                    Kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
                    Summary: "runtime.lock.install.preview.runtime-lock-pinned",
                    SubjectId: "runtime-lock-a",
                    SummaryKey: "runtime.lock.install.preview.runtime-lock-pinned",
                    SummaryParameters:
                    [
                        new RulesetExplainParameter("lockId", RulesetCapabilityBridge.FromObject("runtime-lock-a")),
                        new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:compat-runtime"))
                    ]),
                new RuntimeLockInstallPreviewItem(
                    Kind: RuntimeLockInstallPreviewChangeKinds.SessionReplayRequired,
                    Summary: "runtime.lock.install.preview.session-replay-required",
                    SubjectId: "session-a",
                    RequiresConfirmation: true,
                    SummaryKey: "runtime.lock.install.preview.session-replay-required",
                    SummaryParameters:
                    [
                        new RulesetExplainParameter("targetKind", RulesetCapabilityBridge.FromObject(RuleProfileApplyTargetKinds.SessionLedger)),
                        new RulesetExplainParameter("targetId", RulesetCapabilityBridge.FromObject("session-a"))
                    ])
            ],
            Warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.ProviderBinding,
                    Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                    Message: "runtime.lock.install.warning.runtime-review",
                    SubjectId: "runtime-lock-a",
                    MessageKey: "runtime.lock.install.warning.runtime-review",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("rulePackCount", RulesetCapabilityBridge.FromObject(2))
                    ]),
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Trust,
                    Severity: RuntimeInspectorWarningSeverityLevels.Info,
                    Message: "runtime.lock.install.warning.local-only",
                    SubjectId: "runtime-lock-a",
                    MessageKey: "runtime.lock.install.warning.local-only")
            ],
            RequiresConfirmation: true);
    }

    private static BuildKitManifest CreateBuildKitManifestFixtureA()
    {
        return new BuildKitManifest(
            BuildKitId: "starter-kit",
            Version: "1.0.0",
            Title: "Starter Kit",
            Description: "BuildKit normalization fixture.",
            Targets: [RulesetDefaults.Sr5, " SR5 "],
            RuntimeRequirements:
            [
                new BuildKitRuntimeRequirement(
                    RulesetId: " SR5 ",
                    RequiredRuntimeFingerprints: ["sha256:b", "sha256:a", "sha256:b"],
                    RequiredRulePacks:
                    [
                        new ArtifactVersionReference("house-rules", "1.0.0"),
                        new ArtifactVersionReference("combat-pack", "2.0.0")
                    ])
            ],
            Prompts:
            [
                new BuildKitPromptDescriptor(
                    PromptId: " path ",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: " Path ",
                    Options:
                    [
                        new BuildKitPromptOption(" mage ", " Mage "),
                        new BuildKitPromptOption(" street ", " Street ")
                    ],
                    Required: true),
                new BuildKitPromptDescriptor(
                    PromptId: "priority",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: "Priority",
                    Options:
                    [
                        new BuildKitPromptOption("A", "A"),
                        new BuildKitPromptOption("B", "B")
                    ])
            ],
            Actions:
            [
                new BuildKitActionDescriptor(
                    ActionId: " set-note ",
                    Kind: BuildKitActionKinds.SetMetadata,
                    TargetId: "workspace",
                    Notes: " starter "),
                new BuildKitActionDescriptor(
                    ActionId: "apply-path",
                    Kind: BuildKitActionKinds.ApplyChoice,
                    TargetId: "career-path",
                    PromptId: "path")
            ],
            Visibility: " local-only ",
            TrustTier: " local-only ");
    }

    private static BuildKitManifest CreateBuildKitManifestFixtureB()
    {
        return new BuildKitManifest(
            BuildKitId: "starter-kit",
            Version: "1.0.0",
            Title: "Starter Kit",
            Description: "BuildKit normalization fixture.",
            Targets: ["sr5"],
            RuntimeRequirements:
            [
                new BuildKitRuntimeRequirement(
                    RulesetId: "sr5",
                    RequiredRuntimeFingerprints: ["sha256:a", "sha256:b"],
                    RequiredRulePacks:
                    [
                        new ArtifactVersionReference("combat-pack", "2.0.0"),
                        new ArtifactVersionReference("house-rules", "1.0.0")
                    ])
            ],
            Prompts:
            [
                new BuildKitPromptDescriptor(
                    PromptId: "priority",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: "Priority",
                    Options:
                    [
                        new BuildKitPromptOption("B", "B"),
                        new BuildKitPromptOption("A", "A")
                    ]),
                new BuildKitPromptDescriptor(
                    PromptId: "path",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: "Path",
                    Options:
                    [
                        new BuildKitPromptOption("street", "Street"),
                        new BuildKitPromptOption("mage", "Mage")
                    ],
                    Required: true)
            ],
            Actions:
            [
                new BuildKitActionDescriptor(
                    ActionId: "apply-path",
                    Kind: BuildKitActionKinds.ApplyChoice,
                    TargetId: "career-path",
                    PromptId: "path"),
                new BuildKitActionDescriptor(
                    ActionId: "set-note",
                    Kind: BuildKitActionKinds.SetMetadata,
                    TargetId: "workspace",
                    Notes: "starter")
            ],
            Visibility: ArtifactVisibilityModes.LocalOnly,
            TrustTier: ArtifactTrustTiers.LocalOnly);
    }

    private static RuntimeLockInstallCandidate CreateRuntimeLockInstallCandidateFixtureA()
    {
        return new RuntimeLockInstallCandidate(
            TargetKind: RuleProfileApplyTargetKinds.SessionLedger,
            TargetId: "session-a",
            Entry: CreateRuntimeLockRegistryEntry("runtime-lock-a", CreateCompatibilityRuntimeLockFixtureA()),
            Diagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.MissingPack,
                    Message: "runtime.lock.compatibility.missing-pack",
                    RequiredRulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprint: "sha256:compat-runtime",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("version", RulesetCapabilityBridge.FromObject("1.0.0")),
                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("missing-pack"))
                    ]),
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "runtime.lock.compatibility.rebind-required",
                    RequiredRulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprint: "sha256:compat-runtime")
            ],
            CanInstall: true);
    }

    private static RuntimeLockInstallCandidate CreateRuntimeLockInstallCandidateFixtureB()
    {
        return new RuntimeLockInstallCandidate(
            TargetKind: RuleProfileApplyTargetKinds.SessionLedger,
            TargetId: "session-a",
            Entry: CreateRuntimeLockRegistryEntry("runtime-lock-a", CreateCompatibilityRuntimeLockFixtureB()),
            Diagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "runtime.lock.compatibility.rebind-required",
                    RequiredRulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprint: "sha256:compat-runtime",
                    MessageKey: "runtime.lock.compatibility.rebind-required"),
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.MissingPack,
                    Message: "runtime.lock.compatibility.missing-pack",
                    RequiredRulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprint: "sha256:compat-runtime",
                    MessageKey: "runtime.lock.compatibility.missing-pack",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("missing-pack")),
                        new RulesetExplainParameter("version", RulesetCapabilityBridge.FromObject("1.0.0"))
                    ])
            ],
            CanInstall: false);
    }

    private static ResolvedRuntimeLock CreateCompatibilityRuntimeLockFixtureA()
    {
        return new ResolvedRuntimeLock(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "content-bundle-z",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "1.0.0",
                    Title: "Content Z",
                    Description: "Z bundle",
                    AssetPaths: ["z.xml", "a.xml"]),
                new ContentBundleDescriptor(
                    BundleId: "content-bundle-a",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "1.0.0",
                    Title: "Content A",
                    Description: "A bundle",
                    AssetPaths: ["b.xml", "a.xml"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("house-rules", "1.0.0"),
                new ArtifactVersionReference("combat-pack", "2.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.SessionQuickActions] = "combat-pack/session.quick-actions",
                [RulePackCapabilityIds.DeriveStat] = "house-rules/derive.stat"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "sha256:compat-runtime");
    }

    private static ResolvedRuntimeLock CreateCompatibilityRuntimeLockFixtureB()
    {
        return new ResolvedRuntimeLock(
            RulesetId: " sr5 ",
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "content-bundle-a",
                    RulesetId: " sr5 ",
                    Version: "1.0.0",
                    Title: "Content A",
                    Description: "A bundle",
                    AssetPaths: ["a.xml", "b.xml"]),
                new ContentBundleDescriptor(
                    BundleId: "content-bundle-z",
                    RulesetId: "sr5",
                    Version: "1.0.0",
                    Title: "Content Z",
                    Description: "Z bundle",
                    AssetPaths: ["a.xml", "z.xml"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("combat-pack", "2.0.0"),
                new ArtifactVersionReference("house-rules", "1.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "house-rules/derive.stat",
                [RulePackCapabilityIds.SessionQuickActions] = "combat-pack/session.quick-actions"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "sha256:compat-runtime");
    }

    private static RuntimeLockRegistryEntry CreateRuntimeLockRegistryEntry(string lockId, ResolvedRuntimeLock runtimeLock)
    {
        return new RuntimeLockRegistryEntry(
            LockId: lockId,
            Owner: OwnerScope.LocalSingleUser,
            Title: $"{lockId} title",
            Visibility: ArtifactVisibilityModes.LocalOnly,
            CatalogKind: RuntimeLockCatalogKinds.Saved,
            RuntimeLock: runtimeLock,
            UpdatedAtUtc: DateTimeOffset.UnixEpoch.AddHours(2),
            Install: new ArtifactInstallState(ArtifactInstallStates.Available));
    }

    private static BuildKitRegistryEntry CreateBuildKitRegistryEntry(string buildKitId, BuildKitManifest manifest)
    {
        BuildKitManifest normalizedManifest = RuntimeCompatibilityContractNormalizer.NormalizeBuildKitManifest(
            manifest with { BuildKitId = buildKitId });
        return new BuildKitRegistryEntry(
            Manifest: normalizedManifest,
            Owner: OwnerScope.LocalSingleUser,
            Visibility: ArtifactVisibilityModes.LocalOnly,
            PublicationStatus: BuildKitPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.UnixEpoch.AddHours(2));
    }

    private static string FormatCompatibilityRow(HubProjectCompatibilityRow row)
    {
        return string.Join(
            "|",
            row.Kind,
            row.State,
            row.CurrentValue,
            row.RequiredValue ?? string.Empty,
            row.Notes ?? string.Empty,
            row.LabelKey ?? string.Empty,
            row.CurrentValueKey ?? string.Empty,
            row.RequiredValueKey ?? string.Empty,
            row.NotesKey ?? string.Empty,
            string.Join(",", (row.NotesParameters ?? []).OrderBy(static parameter => parameter.Name, StringComparer.Ordinal).Select(static parameter => $"{parameter.Name}:{parameter.Value.StringValue ?? parameter.Value.IntegerValue?.ToString() ?? string.Empty}")));
    }

    private static string FormatCapabilityProjection(HubProjectCapabilityDescriptorProjection capability)
    {
        return string.Join(
            "|",
            capability.CapabilityId,
            capability.ProviderId ?? string.Empty,
            capability.PackId ?? string.Empty,
            capability.SessionSafe,
            capability.Explainable,
            capability.TitleKey ?? string.Empty);
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly IReadOnlyList<RuleProfileRegistryEntry> _entries;

        public RuleProfileRegistryServiceStub(RuleProfileRegistryEntry entry)
            : this([entry])
        {
        }

        public RuleProfileRegistryServiceStub(IReadOnlyList<RuleProfileRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
            => _entries;

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null)
            => _entries.FirstOrDefault(entry =>
                string.Equals(profileId, entry.Manifest.ProfileId, StringComparison.Ordinal)
                && (rulesetId is null || string.Equals(rulesetId, entry.Manifest.RulesetId, StringComparison.Ordinal)));
    }

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null)
            => _entries.FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));
    }

    private sealed class BuildKitRegistryServiceStub : IBuildKitRegistryService
    {
        private readonly IReadOnlyList<BuildKitRegistryEntry> _entries;

        public BuildKitRegistryServiceStub(IReadOnlyList<BuildKitRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return _entries
                .Where(entry => normalizedRulesetId is null || entry.Manifest.Targets.Any(target => string.Equals(target, normalizedRulesetId, StringComparison.Ordinal)))
                .OrderBy(static entry => entry.Manifest.BuildKitId, StringComparer.Ordinal)
                .ToArray();
        }

        public BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return _entries.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.BuildKitId, buildKitId, StringComparison.Ordinal)
                && (normalizedRulesetId is null || entry.Manifest.Targets.Any(target => string.Equals(target, normalizedRulesetId, StringComparison.Ordinal))));
        }
    }

    private sealed class RuntimeLockRegistryServiceStub : IRuntimeLockRegistryService
    {
        private readonly Dictionary<string, RuntimeLockRegistryEntry> _entries;

        public RuntimeLockRegistryServiceStub(IReadOnlyList<RuntimeLockRegistryEntry> entries)
        {
            _entries = entries.ToDictionary(static entry => entry.LockId, StringComparer.Ordinal);
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null)
        {
            RuntimeLockRegistryEntry[] entries = _entries.Values
                .Where(entry => rulesetId is null || string.Equals(entry.RuntimeLock.RulesetId, rulesetId, StringComparison.Ordinal))
                .OrderBy(static entry => entry.LockId, StringComparer.Ordinal)
                .ToArray();

            return new RuntimeLockRegistryPage(entries, entries.Length);
        }

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
            => _entries.TryGetValue(lockId, out RuntimeLockRegistryEntry? entry)
               && (rulesetId is null || string.Equals(entry.RuntimeLock.RulesetId, rulesetId, StringComparison.Ordinal))
                ? entry
                : null;

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)
        {
            RuntimeLockRegistryEntry persisted = new(
                LockId: lockId,
                Owner: owner,
                Title: request.Title,
                Visibility: request.Visibility,
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                RuntimeLock: request.RuntimeLock,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Description: request.Description,
                Install: request.Install ?? new ArtifactInstallState(ArtifactInstallStates.Available));
            _entries[lockId] = persisted;
            return persisted;
        }
    }

    private sealed class RuntimeLockInstallHistoryStoreStub : IRuntimeLockInstallHistoryStore
    {
        public IReadOnlyList<RuntimeLockInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => [];

        public IReadOnlyList<RuntimeLockInstallHistoryRecord> GetHistory(OwnerScope owner, string lockId, string rulesetId) => [];

        public RuntimeLockInstallHistoryRecord Append(OwnerScope owner, RuntimeLockInstallHistoryRecord record) => record;
    }

    private sealed class RulePackInstallStateStoreStub : IRulePackInstallStateStore
    {
        public IReadOnlyList<RulePackInstallRecord> List(OwnerScope owner, string? rulesetId = null) => [];

        public RulePackInstallRecord? Get(OwnerScope owner, string packId, string version, string rulesetId) => null;

        public RulePackInstallRecord Upsert(OwnerScope owner, RulePackInstallRecord record) => record;
    }

    private sealed class RulePackInstallHistoryStoreStub : IRulePackInstallHistoryStore
    {
        public IReadOnlyList<RulePackInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => [];

        public IReadOnlyList<RulePackInstallHistoryRecord> GetHistory(OwnerScope owner, string packId, string version, string rulesetId) => [];

        public RulePackInstallHistoryRecord Append(OwnerScope owner, RulePackInstallHistoryRecord record) => record;
    }

    private sealed class RuleProfileInstallStateStoreStub : IRuleProfileInstallStateStore
    {
        public IReadOnlyList<RuleProfileInstallRecord> List(OwnerScope owner, string? rulesetId = null) => [];

        public RuleProfileInstallRecord? Get(OwnerScope owner, string profileId, string rulesetId) => null;

        public RuleProfileInstallRecord Upsert(OwnerScope owner, RuleProfileInstallRecord record) => record;
    }

    private sealed class RuleProfileInstallHistoryStoreStub : IRuleProfileInstallHistoryStore
    {
        public IReadOnlyList<RuleProfileInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => [];

        public IReadOnlyList<RuleProfileInstallHistoryRecord> GetHistory(OwnerScope owner, string profileId, string rulesetId) => [];

        public RuleProfileInstallHistoryRecord Append(OwnerScope owner, RuleProfileInstallHistoryRecord record) => record;
    }

    private sealed class RuntimeLockInstallServiceStub : IRuntimeLockInstallService
    {
        public RuntimeLockInstallPreviewReceipt? Preview(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null) => null;

        public RuntimeLockInstallReceipt? Apply(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null) => null;
    }

    private sealed class AiDigestServiceStub : IAiDigestService
    {
        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
        {
            return new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:test-runtime",
                RulesetId: RulesetDefaults.Sr5,
                Title: "Seattle Ops Runtime",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "rulepack-v1",
                ContentBundles: ["official.sr5.base@schema-1"],
                RulePacks: ["combat-pack@1.2.0", "house-rules@1.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RulePackCapabilityIds.DeriveStat] = "combat-pack/derive.initiative"
                },
                Visibility: ArtifactVisibilityModes.LocalOnly,
                Description: "Seeded runtime for explain tests.");
        }

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
        {
            return new AiCharacterDigestProjection(
                CharacterId: "char-1",
                DisplayName: "Rin",
                RulesetId: RulesetDefaults.Sr5,
                RuntimeFingerprint: "sha256:test-runtime",
                Summary: new CharacterFileSummary(
                    Name: "Rin",
                    Alias: "Ghost",
                    Metatype: "Human",
                    BuildMethod: "priority",
                    CreatedVersion: "5.0",
                    AppVersion: "test",
                    Karma: 23,
                    Nuyen: 1500m,
                    Created: true),
                LastUpdatedUtc: DateTimeOffset.UnixEpoch.AddDays(1),
                HasSavedWorkspace: true);
        }

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
        {
            return new AiSessionDigestProjection(
                CharacterId: "char-1",
                DisplayName: "Rin",
                RulesetId: RulesetDefaults.Sr5,
                RuntimeFingerprint: "sha256:test-runtime",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                SessionReady: true,
                BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
                RequiresBundleRefresh: false,
                ProfileId: "official.sr5.ops",
                ProfileTitle: "Official SR5 Ops");
        }
    }

    private sealed class AiDigestServiceMismatchedSessionStub : IAiDigestService
    {
        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
        {
            return new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:test-runtime",
                RulesetId: RulesetDefaults.Sr5,
                Title: "Seattle Ops Runtime",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "rulepack-v1",
                ContentBundles: ["official.sr5.base@schema-1"],
                RulePacks: ["combat-pack@1.2.0", "house-rules@1.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                Visibility: ArtifactVisibilityModes.LocalOnly,
                Description: "Seeded runtime for explain tests.");
        }

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
        {
            return new AiCharacterDigestProjection(
                CharacterId: "char-1",
                DisplayName: "Rin",
                RulesetId: RulesetDefaults.Sr5,
                RuntimeFingerprint: "sha256:test-runtime",
                Summary: new CharacterFileSummary(
                    Name: "Rin",
                    Alias: "Ghost",
                    Metatype: "Human",
                    BuildMethod: "priority",
                    CreatedVersion: "5.0",
                    AppVersion: "test",
                    Karma: 23,
                    Nuyen: 1500m,
                    Created: true),
                LastUpdatedUtc: DateTimeOffset.UnixEpoch.AddDays(1),
                HasSavedWorkspace: true);
        }

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
        {
            return new AiSessionDigestProjection(
                CharacterId: "char-1",
                DisplayName: "Rin",
                RulesetId: RulesetDefaults.Sr5,
                RuntimeFingerprint: "sha256:other-runtime",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                SessionReady: true,
                BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
                RequiresBundleRefresh: false,
                ProfileId: "wrong-profile",
                ProfileTitle: "Wrong Profile");
        }
    }

    private sealed class ExplainTestRulesetPlugin : IRulesetPlugin
    {
        public ExplainTestRulesetPlugin()
        {
            Capabilities = new ExplainTestCapabilityHost();
            Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);
            Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);
        }

        public RulesetId Id { get; } = new(RulesetDefaults.Sr5);

        public string DisplayName => "Explain Test Ruleset";

        public IRulesetSerializer Serializer { get; } = new ExplainTestSerializer();

        public IRulesetShellDefinitionProvider ShellDefinitions { get; } = new ExplainTestShellDefinitions();

        public IRulesetCatalogProvider Catalogs { get; } = new ExplainTestCatalogs();

        public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; } = new ExplainTestCapabilityDescriptors();

        public IRulesetCapabilityHost Capabilities { get; }

        public IRulesetRuleHost Rules { get; }

        public IRulesetScriptHost Scripts { get; }
    }

    private sealed class ExplainTestSerializer : IRulesetSerializer
    {
        public RulesetId RulesetId { get; } = new(RulesetDefaults.Sr5);

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
            => new(RulesetDefaults.Sr5, SchemaVersion, payloadKind, payload);
    }

    private sealed class ExplainTestShellDefinitions : IRulesetShellDefinitionProvider
    {
        public IReadOnlyList<Chummer.Contracts.Presentation.AppCommandDefinition> GetCommands() => [];

        public IReadOnlyList<Chummer.Contracts.Presentation.NavigationTabDefinition> GetNavigationTabs() => [];
    }

    private sealed class ExplainTestCatalogs : IRulesetCatalogProvider
    {
        public IReadOnlyList<Chummer.Contracts.Presentation.WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => [];
    }

    private sealed class ExplainTestCapabilityDescriptors : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors()
        {
            return
            [
                new RulesetCapabilityDescriptor(
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                    Title: "Initiative",
                    Explainable: true,
                    SessionSafe: true,
                    DefaultGasBudget: new RulesetGasBudget(1000, 5000, 1024),
                    MaximumGasBudget: new RulesetGasBudget(2000, 10000, 2048),
                    TitleKey: "ruleset.capability.derive.stat.title")
            ];
        }
    }

    private sealed class ExplainTestCapabilityHost : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                Success: true,
                Output: RulesetCapabilityBridge.FromObject(14),
                Diagnostics:
                [
                    new RulesetCapabilityDiagnostic(
                        Code: "soft-cap",
                        Message: "ruleset.diagnostic.soft-cap",
                        Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                        MessageKey: "ruleset.diagnostic.soft-cap",
                        MessageParameters:
                        [
                            new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(request.CapabilityId))
                        ])
                ],
                Explain: new RulesetExplainTrace(
                    TargetKey: "initiative.total",
                    FinalValue: RulesetCapabilityBridge.FromObject(14),
                    SummaryKey: "ruleset.explain.summary.derived-value",
                    SummaryParameters:
                    [
                        new RulesetExplainParameter("targetKey", RulesetCapabilityBridge.FromObject("initiative.total")),
                        new RulesetExplainParameter("finalValue", RulesetCapabilityBridge.FromObject(14))
                    ],
                    Providers:
                    [
                        new RulesetProviderTrace(
                            ProviderId: "combat-pack/derive.initiative",
                            CapabilityId: request.CapabilityId,
                            PackId: "combat-pack",
                            Success: true,
                            Steps:
                            [
                                new RulesetTraceStep(
                                    ProviderId: "combat-pack/derive.initiative",
                                    CapabilityId: request.CapabilityId,
                                    PackId: "combat-pack",
                                    ExplanationKey: "ruleset.trace.initiative.base",
                                    ExplanationParameters:
                                    [
                                        new RulesetExplainParameter("reaction", RulesetCapabilityBridge.FromObject(5)),
                                        new RulesetExplainParameter("intuition", RulesetCapabilityBridge.FromObject(4))
                                    ],
                                    Category: "derived-value",
                                    Modifier: 9m,
                                    Certain: true,
                                    RuleId: "sr5.combat.initiative",
                                    Evidence:
                                    [
                                        new RulesetEvidencePointer(
                                            Kind: RulesetEvidencePointerKinds.RuleReference,
                                            Pointer: "sr5.combat.initiative",
                                            LabelKey: "ruleset.explain.evidence.rule-reference",
                                            LabelParameters:
                                            [
                                                new RulesetExplainParameter("ruleId", RulesetCapabilityBridge.FromObject("sr5.combat.initiative"))
                                            ],
                                            ProviderId: "combat-pack/derive.initiative",
                                            PackId: "combat-pack",
                                            RuleId: "sr5.combat.initiative")
                                    ])
                            ],
                            GasUsage: new RulesetGasUsage(100, 220, 4096),
                            Evidence:
                            [
                                new RulesetEvidencePointer(
                                    Kind: RulesetEvidencePointerKinds.RulePack,
                                    Pointer: "combat-pack",
                                    LabelKey: "ruleset.explain.evidence.rulepack",
                                    LabelParameters:
                                    [
                                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("combat-pack"))
                                    ],
                                    ProviderId: "combat-pack/derive.initiative",
                                    PackId: "combat-pack")
                            ])
                    ],
                    AggregateGasUsage: new RulesetGasUsage(100, 220, 4096),
                    RuntimeFingerprint: "sha256:test-runtime",
                    ProfileId: "official.sr5.ops",
                    Evidence:
                    [
                        new RulesetEvidencePointer(
                            Kind: RulesetEvidencePointerKinds.RuntimeLock,
                            Pointer: "sha256:test-runtime",
                            LabelKey: "ruleset.explain.evidence.runtime-lock",
                            LabelParameters:
                            [
                                new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:test-runtime"))
                            ]),
                        new RulesetEvidencePointer(
                            Kind: RulesetEvidencePointerKinds.RuleProfile,
                            Pointer: "official.sr5.ops",
                            LabelKey: "ruleset.explain.evidence.rule-profile",
                            LabelParameters:
                            [
                                new RulesetExplainParameter("profileId", RulesetCapabilityBridge.FromObject("official.sr5.ops"))
                            ])
                    ])));
        }
    }

    private static string ToComparableChange(RuntimeLockDiffChange change)
        => string.Join(
            "|",
            change.Kind,
            change.SubjectId,
            change.BeforeValue ?? string.Empty,
            change.AfterValue ?? string.Empty,
            change.ReasonKey,
            string.Join(
                ",",
                change.ReasonParameters
                    .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                    .Select(parameter => $"{parameter.Name}:{parameter.Value.Kind}:{parameter.Value.StringValue ?? parameter.Value.IntegerValue?.ToString() ?? parameter.Value.NumberValue?.ToString() ?? parameter.Value.DecimalValue?.ToString() ?? parameter.Value.BooleanValue?.ToString() ?? "null"}")));

    private static AiRuntimeSummaryProjection CreateRuntimeSummaryFixture()
    {
        return new AiRuntimeSummaryProjection(
            RuntimeFingerprint: "sha256:golden-runtime",
            RulesetId: RulesetDefaults.Sr5,
            Title: "Official SR5 Runtime",
            CatalogKind: RuntimeLockCatalogKinds.Published,
            EngineApiVersion: "engine-v2",
            ContentBundles: ["core", "street-wyrd"],
            RulePacks: ["combat-pack", "quality-pack"],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "combat-pack/derive.initiative",
                [RulePackCapabilityIds.SessionQuickActions] = "combat-pack/session.quickaction"
            },
            Visibility: ArtifactVisibilityModes.Public,
            Description: "Canonical runtime fixture");
    }

    private static ExplainTraceDto CreateExplainTraceFixture()
    {
        ExplainProvenanceDto provenance = new(
            RuntimeFingerprint: "sha256:golden-runtime",
            RulesetId: RulesetDefaults.Sr5,
            EngineApiVersion: "engine-v2",
            CatalogKind: RuntimeLockCatalogKinds.Published,
            RuntimeTitle: "Official SR5 Runtime",
            ProfileId: "official.sr5.ops",
            ProfileTitle: "Operations Profile",
            ProviderId: "combat-pack/derive.initiative",
            PackId: "combat-pack",
            RulePacks: ["combat-pack", "quality-pack"],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "combat-pack/derive.initiative",
                [RulePackCapabilityIds.SessionQuickActions] = "combat-pack/session.quickaction"
            });
        ExplainEvidencePointerDto[] evidence =
        [
            new ExplainEvidencePointerDto(
                Kind: RulesetEvidencePointerKinds.RuntimeLock,
                Pointer: "sha256:golden-runtime",
                LabelKey: "ruleset.explain.evidence.runtime-lock",
                LabelParameters:
                [
                    new RulesetExplainParameter("runtimeFingerprint", RulesetCapabilityBridge.FromObject("sha256:golden-runtime"))
                ]),
            new ExplainEvidencePointerDto(
                Kind: RulesetEvidencePointerKinds.RuleProfile,
                Pointer: "official.sr5.ops",
                LabelKey: "ruleset.explain.evidence.rule-profile",
                LabelParameters:
                [
                    new RulesetExplainParameter("profileId", RulesetCapabilityBridge.FromObject("official.sr5.ops"))
                ])
        ];

        return new ExplainTraceDto(
            TargetKey: "initiative.total",
            FinalValue: RulesetCapabilityBridge.FromObject(14),
            SummaryKey: "ruleset.explain.summary.derived-value",
            SummaryParameters:
            [
                new RulesetExplainParameter("targetKey", RulesetCapabilityBridge.FromObject("initiative.total")),
                new RulesetExplainParameter("finalValue", RulesetCapabilityBridge.FromObject(14))
            ],
            Steps:
            [
                new TraceStepDto(
                    ProviderId: "combat-pack/derive.initiative",
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    PackId: "combat-pack",
                    ExplanationKey: "ruleset.trace.initiative.base",
                    ExplanationParameters:
                    [
                        new RulesetExplainParameter("reaction", RulesetCapabilityBridge.FromObject(5)),
                        new RulesetExplainParameter("intuition", RulesetCapabilityBridge.FromObject(4))
                    ],
                    Category: "derived-value",
                    Modifier: 9m,
                    Certain: true,
                    RuleId: "sr5.combat.initiative",
                    Evidence:
                    [
                        new ExplainEvidencePointerDto(
                            Kind: RulesetEvidencePointerKinds.RuleReference,
                            Pointer: "sr5.combat.initiative",
                            LabelKey: "ruleset.explain.evidence.rule-reference",
                            LabelParameters:
                            [
                                new RulesetExplainParameter("ruleId", RulesetCapabilityBridge.FromObject("sr5.combat.initiative"))
                            ],
                            ProviderId: "combat-pack/derive.initiative",
                            PackId: "combat-pack",
                            RuleId: "sr5.combat.initiative")
                    ]),
                new TraceStepDto(
                    ProviderId: "combat-pack/derive.initiative",
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    PackId: "combat-pack",
                    ExplanationKey: "ruleset.diagnostic.soft-cap",
                    ExplanationParameters:
                    [
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(RulePackCapabilityIds.DeriveStat))
                    ],
                    Category: "diagnostic",
                    Modifier: null,
                    Certain: null,
                    RuleId: null)
            ],
            Provenance: provenance,
            Evidence: evidence,
            ProvenanceEnvelope: new ExplainProvenanceEnvelopeDto(
                Schema: ExplainEnvelopeSchemas.ProvenanceV1,
                Provenance: provenance,
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                ProviderId: "combat-pack/derive.initiative",
                PackId: "combat-pack"),
            EvidenceEnvelope: new ExplainEvidenceEnvelopeDto(
                Schema: ExplainEnvelopeSchemas.EvidenceV1,
                Pointers: evidence,
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                ProviderId: "combat-pack/derive.initiative",
                PackId: "combat-pack"));
    }

    private static RuntimeLockDiffProjection CreateRuntimeLockDiffFixture()
    {
        DefaultRuntimeLockDiffService service = new();

        ResolvedRuntimeLock before = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-1",
                    Title: "SR5 Base",
                    Description: "Built-in base content.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("combat-pack", "1.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "combat-pack/derive.initiative"
            },
            EngineApiVersion: "engine-v1",
            RuntimeFingerprint: "sha256:before");
        ResolvedRuntimeLock after = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "campaign.seattle",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03",
                    Title: "Seattle Campaign",
                    Description: "Campaign bundle.",
                    AssetPaths: ["data/", "media/"]),
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-1",
                    Title: "SR5 Base",
                    Description: "Built-in base content.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("combat-pack", "1.1.0"),
                new ArtifactVersionReference("quality-pack", "2.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "combat-pack/derive.initiative.v2",
                [RulePackCapabilityIds.SessionQuickActions] = "quality-pack/session.quickaction"
            },
            EngineApiVersion: "engine-v2",
            RuntimeFingerprint: "sha256:after");

        return service.Diff(before, after);
    }

    private static SessionLedger CreateSessionLedgerFixture()
    {
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: "char-1",
            VersionId: "ver-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "sha256:golden-runtime");

        return new SessionLedger(
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            Events:
            [
                new SessionEventEnvelope(
                    EventId: "evt-1",
                    OverlayId: "overlay-1",
                    BaseCharacterVersion: baseCharacterVersion,
                    DeviceId: "device-1",
                    ActorId: "actor-1",
                    Sequence: 1,
                    EventType: SessionEventTypes.TrackerIncrement,
                    Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
                    {
                        ["amount"] = RulesetCapabilityBridge.FromObject(2),
                        ["trackerId"] = RulesetCapabilityBridge.FromObject("stun")
                    },
                    CreatedAtUtc: DateTimeOffset.Parse("2026-03-09T00:00:00+00:00"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-09T00:00:03+00:00"),
                    ParentEventId: null,
                    SyncCursor: "cursor-1",
                    ProviderId: "combat-pack/session.quickaction",
                    PackId: "combat-pack",
                    Schema: SessionEventEnvelopeSchemas.SessionEventsVnext)
            ],
            BaselineSnapshotId: "snapshot-1",
            NextSequence: 2);
    }

    private static void AssertGoldenJsonFixture(string fixtureName, object value)
    {
        string fixturePath = Path.Combine(GetRepositoryRoot(), "Chummer.CoreEngine.Tests", "Fixtures", "Contracts", fixtureName);
        string expected = File.ReadAllText(fixturePath).Replace("\r\n", "\n").TrimEnd();
        string actual = JsonSerializer.Serialize(value, GoldenJsonOptions).Replace("\r\n", "\n").TrimEnd();
        AssertEx.Equal(expected, actual, $"Golden JSON fixture '{fixtureName}' drifted.");
    }

    private static void RepoBoundaryGuardsHostedContractsAndSharedContractOwnership()
    {
        string repositoryRoot = GetRepositoryRoot();
        string coreContractsRoot = Path.Combine(repositoryRoot, "Chummer.Contracts");
        string runContractsRoot = Path.Combine(repositoryRoot, "Chummer.Run.Contracts");
        string presentationContractsRoot = Path.Combine(repositoryRoot, "Chummer.Presentation.Contracts");
        string runServicesContractsRoot = Path.Combine(repositoryRoot, "Chummer.RunServices.Contracts");
        AssertEx.True(!Directory.Exists(presentationContractsRoot), "Temporary project root 'Chummer.Presentation.Contracts' should be deleted.");
        AssertEx.True(!Directory.Exists(runServicesContractsRoot), "Temporary project root 'Chummer.RunServices.Contracts' should be deleted.");

        string corePresentationContractsDirectory = Path.Combine(coreContractsRoot, "Presentation");
        string[] canonicalPresentationContracts =
        [
            "AppCommandCatalogResponse.cs",
            "AppCommandDefinition.cs",
            "AppCommandIds.cs",
            "NavigationTabCatalogResponse.cs",
            "NavigationTabDefinition.cs",
            "WorkflowSurfaceContracts.cs",
            "WorkspaceSurfaceActionDefinition.cs",
            "BrowseQueryContracts.cs",
            "BrowseWorkspaceContracts.cs",
            "BuildKitWorkbenchContracts.cs",
            "DesignTokenContracts.cs",
            "JournalPanelContracts.cs",
            "RulePackWorkbenchContracts.cs",
            "ShellBootstrapContracts.cs"
        ];

        foreach (string fileName in canonicalPresentationContracts)
        {
            AssertEx.True(
                File.Exists(Path.Combine(corePresentationContractsDirectory, fileName)),
                $"Canonical presentation contract '{fileName}' should live under Chummer.Contracts/Presentation.");
        }

        string coreAiContractsDirectory = Path.Combine(runContractsRoot, "AI");
        string[] canonicalAiContracts =
        [
            "AiActionPreviewContracts.cs",
            "AiApprovalContracts.cs",
            "AiBuildIdeaCatalogContracts.cs",
            "AiCoachLaunchContracts.cs",
            "AiConversationCatalogContracts.cs",
            "AiDigestContracts.cs",
            "AiEvaluationContracts.cs",
            "AiExplainContracts.cs",
            "AiGatewayContracts.cs",
            "AiHistoryDraftContracts.cs",
            "AiHubProjectSearchContracts.cs",
            "AiMediaAssetContracts.cs",
            "AiMediaContracts.cs",
            "AiMediaQueueContracts.cs",
            "AiPortraitPromptContracts.cs",
            "AiPromptRegistryContracts.cs",
            "AiRecapDraftContracts.cs",
            "AiTranscriptContracts.cs",
            "BuildIdeaCardContracts.cs"
        ];

        foreach (string fileName in canonicalAiContracts)
        {
            AssertEx.True(
                File.Exists(Path.Combine(coreAiContractsDirectory, fileName)),
                $"Canonical AI contract '{fileName}' should live under Chummer.Run.Contracts/AI.");
        }

        string coreHubContractsDirectory = Path.Combine(runContractsRoot, "Hub");
        string[] canonicalHubContracts =
        [
            "HubCatalogContracts.cs",
            "HubProjectCompatibilityContracts.cs",
            "HubProjectDetailContracts.cs",
            "HubProjectInstallPreviewContracts.cs",
            "HubPublicationContracts.cs",
            "HubPublisherContracts.cs",
            "HubReviewContracts.cs"
        ];

        foreach (string fileName in canonicalHubContracts)
        {
            AssertEx.True(
                File.Exists(Path.Combine(coreHubContractsDirectory, fileName)),
                $"Canonical hub contract '{fileName}' should live under Chummer.Run.Contracts/Hub.");
        }

        string coreContentContractsDirectory = Path.Combine(coreContractsRoot, "Content");
        string[] coreOwnedRuntimeInstallAndBuildKitContracts =
        [
            "RuntimeLockInstallContracts.cs",
            "RuntimeLockRegistryContracts.cs",
            "BuildKitRegistryContracts.cs",
            "BuildKitApplicationContracts.cs",
            "ArtifactContracts.cs"
        ];

        foreach (string fileName in coreOwnedRuntimeInstallAndBuildKitContracts)
        {
            string canonicalPath = Path.Combine(coreContentContractsDirectory, fileName);
            AssertEx.True(
                File.Exists(canonicalPath),
                $"A6.1 canonical contract '{fileName}' should live under Chummer.Contracts/Content.");
        }

        string runtimeLockInstallContractsText = File.ReadAllText(Path.Combine(coreContentContractsDirectory, "RuntimeLockInstallContracts.cs"));
        AssertEx.True(
            runtimeLockInstallContractsText.Contains("BuildKitSelection", StringComparison.Ordinal)
            && runtimeLockInstallContractsText.Contains("RuntimeLockInstallReceipt", StringComparison.Ordinal),
            "Runtime lock install DTO ownership should keep BuildKit-selection install payloads in Chummer.Contracts.");

        string runtimeLockRegistryContractsText = File.ReadAllText(Path.Combine(coreContentContractsDirectory, "RuntimeLockRegistryContracts.cs"));
        AssertEx.True(
            runtimeLockRegistryContractsText.Contains("RuntimeLockCompatibilityDiagnostic", StringComparison.Ordinal)
            && runtimeLockRegistryContractsText.Contains("RuntimeLockInstallPreviewReceipt", StringComparison.Ordinal),
            "Runtime compatibility diagnostics and install preview receipts should remain engine-owned contracts.");

        string buildKitApplicationContractsText = File.ReadAllText(Path.Combine(coreContentContractsDirectory, "BuildKitApplicationContracts.cs"));
        AssertEx.True(
            buildKitApplicationContractsText.Contains("BuildKitValidationReceipt", StringComparison.Ordinal)
            && buildKitApplicationContractsText.Contains("BuildKitApplicationReceipt", StringComparison.Ordinal),
            "BuildKit validation and application DTOs should remain engine-owned contracts.");

        string buildKitManifestContractsText = File.ReadAllText(Path.Combine(coreContentContractsDirectory, "ArtifactContracts.cs"));
        AssertEx.True(
            buildKitManifestContractsText.Contains("BuildKitManifest", StringComparison.Ordinal)
            && buildKitManifestContractsText.Contains("BuildKitRuntimeRequirement", StringComparison.Ordinal),
            "BuildKit manifest and runtime requirement DTOs should remain engine-owned contracts.");

        string buildKitWorkbenchContractsPath = Path.Combine(corePresentationContractsDirectory, "BuildKitWorkbenchContracts.cs");
        AssertEx.True(
            File.Exists(buildKitWorkbenchContractsPath),
            "BuildKit workbench projection contracts should remain canonical under Chummer.Contracts/Presentation.");

        string hubCompatibilityContractsPath = Path.Combine(coreHubContractsDirectory, "HubProjectCompatibilityContracts.cs");
        AssertEx.True(
            File.Exists(hubCompatibilityContractsPath),
            "Hub compatibility matrix projections should remain canonical under Chummer.Run.Contracts/Hub.");

        string hubInstallPreviewContractsPath = Path.Combine(coreHubContractsDirectory, "HubProjectInstallPreviewContracts.cs");
        AssertEx.True(
            File.Exists(hubInstallPreviewContractsPath),
            "Hub install preview projections should remain canonical under Chummer.Run.Contracts/Hub.");

        string coreContentContractsText = string.Join(
            "\n",
            Directory.EnumerateFiles(coreContentContractsDirectory, "*.cs", SearchOption.TopDirectoryOnly).Select(File.ReadAllText));
        AssertEx.True(
            !coreContentContractsText.Contains("BuildKitWorkbenchSurfaceIds", StringComparison.Ordinal)
            && !coreContentContractsText.Contains("HubProjectCompatibilityMatrix", StringComparison.Ordinal)
            && !coreContentContractsText.Contains("HubProjectInstallPreviewReceipt", StringComparison.Ordinal),
            "Presentation and run-services compatibility projection DTOs must not leak into engine-owned content contracts.");

        string[] hostedContractSources = Directory
            .EnumerateFiles(runContractsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}AI{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}Hub{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(
                Directory.EnumerateFiles(coreContractsRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Presentation{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            .ToArray();

        foreach (string hostedContractSource in hostedContractSources)
        {
            string fileName = Path.GetFileName(hostedContractSource);
            string[] duplicates = Directory.EnumerateFiles(repositoryRoot, fileName, SearchOption.AllDirectories)
                .Where(path => !IsGeneratedOrBuildArtifact(path))
                .Where(path => !string.Equals(path, hostedContractSource, StringComparison.Ordinal))
                .ToArray();

            AssertEx.True(
                duplicates.Length == 0,
                $"Canonical contract '{fileName}' was duplicated outside Chummer.Contracts/Chummer.Run.Contracts ownership: {string.Join(", ", duplicates.Select(path => Path.GetRelativePath(repositoryRoot, path)))}.");
        }

        string[] projectPaths = Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildArtifact(path))
            .ToArray();

        foreach (string projectPath in projectPaths)
        {
            string projectText = File.ReadAllText(projectPath);
            AssertEx.True(
                !projectText.Contains(@"..\Chummer.RunServices.Contracts\AI\", StringComparison.Ordinal)
                && !projectText.Contains(@"../Chummer.RunServices.Contracts/AI/", StringComparison.Ordinal)
                && !projectText.Contains(@"..\Chummer.RunServices.Contracts\Hub\", StringComparison.Ordinal)
                && !projectText.Contains(@"../Chummer.RunServices.Contracts/Hub/", StringComparison.Ordinal),
                $"Project '{Path.GetRelativePath(repositoryRoot, projectPath)}' must not depend on deleted temporary contract project paths.");
            AssertEx.True(
                !projectText.Contains(@"..\Chummer.Run.Contracts\AI\", StringComparison.Ordinal)
                && !projectText.Contains(@"../Chummer.Run.Contracts/AI/", StringComparison.Ordinal)
                && !projectText.Contains(@"..\Chummer.Run.Contracts\Hub\", StringComparison.Ordinal)
                && !projectText.Contains(@"../Chummer.Run.Contracts/Hub/", StringComparison.Ordinal),
                $"Project '{Path.GetRelativePath(repositoryRoot, projectPath)}' should consume canonical contracts via assembly reference, not by compiling individual AI/Hub source files directly.");
        }
    }

    private static void HardeningBacklogStaysMilestoneMapped()
    {
        string repoRoot = GetRepositoryRoot();
        string worklistText = File.ReadAllText(Path.Combine(repoRoot, "WORKLIST.md"));
        string queueText = File.ReadAllText(Path.Combine(repoRoot, ".codex-studio", "published", "QUEUE.generated.yaml"));
        string designText = File.ReadAllText(Path.Combine(repoRoot, "chummer-core-engine.design.v2.md"));
        string projectMilestonesText = File.ReadAllText(Path.Combine(repoRoot, ".codex-design", "repo", "PROJECT_MILESTONES.yaml"));

        AssertEx.True(
            worklistText.Contains("WL-068", StringComparison.Ordinal)
            && worklistText.Contains("Milestone A6: contract hardening", StringComparison.Ordinal)
            && worklistText.Contains("WL-073", StringComparison.Ordinal)
            && worklistText.Contains("A6.1 canonicalize runtime install and BuildKit DTO ownership", StringComparison.Ordinal)
            && worklistText.Contains("WL-074", StringComparison.Ordinal)
            && worklistText.Contains("A6.2 add normalization fixtures for runtime install, BuildKit, and runtime compatibility DTOs", StringComparison.Ordinal)
            && worklistText.Contains("WL-075", StringComparison.Ordinal)
            && worklistText.Contains("A6.3 harden session/runtime compatibility projection seams", StringComparison.Ordinal)
            && worklistText.Contains("WL-069", StringComparison.Ordinal)
            && worklistText.Contains("Milestone A7: Structured Explain API hardening", StringComparison.Ordinal)
            && worklistText.Contains("WL-076", StringComparison.Ordinal)
            && worklistText.Contains("A7.1 expose keyed disabled-reason payloads across explainable selection/filter surfaces", StringComparison.Ordinal)
            && worklistText.Contains("WL-077", StringComparison.Ordinal)
            && worklistText.Contains("A7.2 lock explain provenance and evidence envelopes", StringComparison.Ordinal)
            && worklistText.Contains("WL-078", StringComparison.Ordinal)
            && worklistText.Contains("A7.3 add before/after runtime diff explain fixtures", StringComparison.Ordinal)
            && worklistText.Contains("WL-070", StringComparison.Ordinal)
            && worklistText.Contains("Milestone A8: Runtime/RulePack determinism hardening", StringComparison.Ordinal)
            && worklistText.Contains("WL-079", StringComparison.Ordinal)
            && worklistText.Contains("A8.1 harden runtime fingerprint byte-stability across ordering variance", StringComparison.Ordinal)
            && worklistText.Contains("WL-080", StringComparison.Ordinal)
            && worklistText.Contains("A8.2 add compile-order and provider-binding determinism tests", StringComparison.Ordinal)
            && worklistText.Contains("WL-081", StringComparison.Ordinal)
            && worklistText.Contains("A8.3 harden RulePack dependency resolution ordering", StringComparison.Ordinal)
            && worklistText.Contains("WL-071", StringComparison.Ordinal)
            && worklistText.Contains("Milestone A9: backend integration primitives", StringComparison.Ordinal)
            && worklistText.Contains("WL-082", StringComparison.Ordinal)
            && worklistText.Contains("A9.1 add journal/ledger timeline projection primitives", StringComparison.Ordinal)
            && worklistText.Contains("WL-083", StringComparison.Ordinal)
            && worklistText.Contains("A9.2 add validation summary and failure-envelope primitives", StringComparison.Ordinal)
            && worklistText.Contains("WL-084", StringComparison.Ordinal)
            && worklistText.Contains("A9.3 add explain-hook composition seam for backend integrations", StringComparison.Ordinal)
            && worklistText.Contains("WL-072", StringComparison.Ordinal)
            && worklistText.Contains("delete temporary contract source projects after package cutover", StringComparison.Ordinal),
            "Worklist backlog should keep remaining hardening and integration scope decomposed into executable milestones.");
        AssertEx.True(
            designText.Contains("### Milestone A6", StringComparison.Ordinal)
            && designText.Contains("### Milestone A7", StringComparison.Ordinal)
            && designText.Contains("### Milestone A8", StringComparison.Ordinal)
            && designText.Contains("### Milestone A9", StringComparison.Ordinal),
            "Design milestones should explicitly cover the remaining hardening and integration scope.");
        AssertEx.True(
            queueText.Contains("Milestones A6-A9", StringComparison.Ordinal)
            && queueText.Contains("A6.1-A6.3", StringComparison.Ordinal)
            && queueText.Contains("A7.1-A7.3", StringComparison.Ordinal)
            && queueText.Contains("A8.1-A8.3", StringComparison.Ordinal)
            && queueText.Contains("A9.1-A9.3", StringComparison.Ordinal),
            "Published queue overlay should point at the concrete A6-A9 milestone decomposition.");
        AssertEx.True(
            !queueText.Contains("Remaining hardening and integration work is still tracked as coarse queue slices rather than milestone-mapped task coverage", StringComparison.Ordinal),
            "Published queue overlay should not regress back to the coarse hardening/integration queue slice.");
        AssertEx.True(
            !queueText.Contains("Cross-repo contract reset work is not yet represented as explicit core milestones", StringComparison.Ordinal),
            "Published queue overlay should keep cross-repo contract reset follow-through mapped to explicit executable milestones.");
        AssertEx.True(
            queueText.Contains("Milestone `A0.5`", StringComparison.Ordinal)
            && queueText.Contains("`WL-072`", StringComparison.Ordinal)
            && queueText.Contains("Chummer.Presentation.Contracts", StringComparison.Ordinal)
            && queueText.Contains("Chummer.RunServices.Contracts", StringComparison.Ordinal)
            && !queueText.Contains("Temporary source-project leaks such as `Chummer.Presentation.Contracts` and `Chummer.RunServices.Contracts` still need deletion after the contract plane cutover.", StringComparison.Ordinal),
            "Published queue overlay should map temporary contract source-project deletion to the executable A0.5/WL-072 follow-through item.");
        AssertEx.True(
            projectMilestonesText.Contains("milestone_coverage_complete: true", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A0.5", StringComparison.Ordinal)
            && projectMilestonesText.Contains("WL-072", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A6", StringComparison.Ordinal)
            && projectMilestonesText.Contains("work_items:", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A6.1", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-073", StringComparison.Ordinal)
            && projectMilestonesText.Contains("status: done", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A6.1 locked canonical ownership line", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A6.2", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-074", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A6.3", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-075", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A7", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A7.1", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-076", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A7.2", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-077", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A7.3", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-078", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A8", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A8.1", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-079", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A8.2", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-080", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A8.3", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-081", StringComparison.Ordinal)
            && projectMilestonesText.Contains("A9", StringComparison.Ordinal),
            "Project milestone registry should map the A0 follow-through and remaining A6-A9 work explicitly.");
        AssertEx.True(
            projectMilestonesText.Contains("id: A9.1", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-082", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A9.2", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-083", StringComparison.Ordinal)
            && projectMilestonesText.Contains("id: A9.3", StringComparison.Ordinal)
            && projectMilestonesText.Contains("worklist: WL-084", StringComparison.Ordinal),
            "Project milestone registry should map the A0 follow-through and remaining A6-A9 work explicitly.");
    }

    private static void ActiveCoreEngineSolutionStaysPurified()
    {
        string repoRoot = GetRepositoryRoot();
        string solutionText = File.ReadAllText(Path.Combine(repoRoot, "Chummer.CoreEngine.sln"));
        string scopeText = File.ReadAllText(Path.Combine(repoRoot, ".codex-design", "repo", "IMPLEMENTATION_SCOPE.md"));
        string projectMilestonesText = File.ReadAllText(Path.Combine(repoRoot, ".codex-design", "repo", "PROJECT_MILESTONES.yaml"));

        string[] excludedSolutionProjects =
        [
            "Chummer.Presentation.Contracts",
            "Chummer.RunServices.Contracts",
            "Chummer.Infrastructure.Browser",
            "ChummerDataViewer",
            "CrashHandler",
            "TextblockConverter",
            "Translator"
        ];

        foreach (string projectName in excludedSolutionProjects)
        {
            AssertEx.True(
                !solutionText.Contains($""{projectName}"", StringComparison.Ordinal),
                $"Active core engine solution must not directly own non-engine project '{projectName}'.");
        }

        string[] quarantinedSurfaces =
        [
            "Chummer.Presentation.Contracts",
            "Chummer.RunServices.Contracts",
            "Chummer.Infrastructure.Browser"
        ];

        foreach (string surface in quarantinedSurfaces)
        {
            AssertEx.True(
                scopeText.Contains(surface, StringComparison.Ordinal)
                || projectMilestonesText.Contains(surface, StringComparison.Ordinal),
                $"Implementation scope or milestone registry should explicitly classify '{surface}' as quarantined non-engine scope.");
            AssertEx.True(
                projectMilestonesText.Contains(surface, StringComparison.Ordinal),
                $"Project milestone registry should explicitly map quarantined surface '{surface}'.");
        }

        string[] retiredHelperRoots =
        [
            "ChummerDataViewer",
            "CrashHandler",
            "TextblockConverter",
            "Translator"
        ];

        foreach (string surface in retiredHelperRoots)
        {
            AssertEx.True(
                !Directory.Exists(Path.Combine(repoRoot, surface)),
                $"Retired helper root '{surface}' must be removed from the core repo.");
            AssertEx.True(
                projectMilestonesText.Contains(surface, StringComparison.Ordinal),
                $"Project milestone registry should explicitly record retired helper surface '{surface}'.");
        }
    }

    private static bool IsGeneratedOrBuildArtifact(string path)
    {
        string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalizedPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || normalizedPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || normalizedPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "instructions.md")))
            {
                return directory;
            }

            string? parent = Directory.GetParent(directory)?.FullName;
            if (string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            directory = parent ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate repository root for fixture loading.");
    }

    private static JsonSerializerOptions GoldenJsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

internal static class AssertEx
{
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    public static void NotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        T[] expectedItems = expected.ToArray();
        T[] actualItems = actual.ToArray();
        if (!expectedItems.SequenceEqual(actualItems))
        {
            throw new InvalidOperationException(
                $"{message} Expected: [{string.Join(", ", expectedItems)}]. Actual: [{string.Join(", ", actualItems)}].");
        }
    }
}
