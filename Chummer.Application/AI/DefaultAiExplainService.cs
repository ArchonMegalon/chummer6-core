using Chummer.Contracts.AI;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.AI;

public sealed class DefaultAiExplainService : IAiExplainService
{
    private readonly IAiDigestService _aiDigestService;
    private readonly IRulesetPluginRegistry _rulesetPluginRegistry;

    public DefaultAiExplainService(
        IAiDigestService aiDigestService,
        IRulesetPluginRegistry rulesetPluginRegistry)
    {
        _aiDigestService = aiDigestService;
        _rulesetPluginRegistry = rulesetPluginRegistry;
    }

    public AiExplainValueProjection? GetExplainValue(OwnerScope owner, AiExplainValueQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? characterId = NormalizeOptional(query.CharacterId);
        AiCharacterDigestProjection? characterDigest = characterId is null
            ? null
            : _aiDigestService.GetCharacterDigest(owner, characterId);
        string? runtimeFingerprint = NormalizeOptional(query.RuntimeFingerprint) ?? characterDigest?.RuntimeFingerprint;
        if (runtimeFingerprint is null)
        {
            return null;
        }

        string? rulesetId = RulesetDefaults.NormalizeOptional(query.RulesetId) ?? characterDigest?.RulesetId;
        AiRuntimeSummaryProjection? runtimeSummary = _aiDigestService.GetRuntimeSummary(owner, runtimeFingerprint, rulesetId);
        if (runtimeSummary is null)
        {
            return null;
        }

        IRulesetPlugin? plugin = _rulesetPluginRegistry.Resolve(runtimeSummary.RulesetId);
        if (plugin is null)
        {
            return null;
        }

        AiSessionDigestProjection? sessionDigest = characterId is null
            ? null
            : _aiDigestService.GetSessionDigest(owner, characterId);
        sessionDigest = AlignSessionDigest(sessionDigest, runtimeSummary);
        string? requestedCapabilityId = NormalizeOptional(query.CapabilityId) ?? NormalizeOptional(query.ExplainEntryId);
        if (requestedCapabilityId is null)
        {
            return null;
        }

        RulesetCapabilityDescriptor? descriptor = plugin.CapabilityDescriptors
            .GetCapabilityDescriptors()
            .FirstOrDefault(candidate => string.Equals(candidate.CapabilityId, requestedCapabilityId, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return null;
        }

        string? providerId = runtimeSummary.ProviderBindings.GetValueOrDefault(descriptor.CapabilityId);
        RulesetCapabilityInvocationResult invocation = plugin.Capabilities
            .InvokeAsync(
                new RulesetCapabilityInvocationRequest(
                    CapabilityId: descriptor.CapabilityId,
                    InvocationKind: descriptor.InvocationKind,
                    Arguments: BuildInvocationArguments(runtimeSummary, characterDigest, query),
                    Options: new RulesetExecutionOptions(Explain: true),
                    ProviderId: providerId,
                    Source: AiExplainApiOperations.ExplainValue),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        string? resolvedProviderId = ResolveProviderId(providerId, invocation.Explain);
        string? packId = ResolvePackId(resolvedProviderId, invocation.Explain, runtimeSummary.RulePacks);
        string explainEntryId = NormalizeOptional(query.ExplainEntryId)
            ?? NormalizeOptional(invocation.Explain?.TargetKey)
            ?? descriptor.CapabilityId;
        string summaryKey = invocation.Explain?.SummaryKey
            ?? (invocation.Diagnostics.Count > 0 ? "ruleset.explain.summary.diagnostic" : "ruleset.explain.summary.default");
        IReadOnlyList<RulesetExplainParameter> summaryParameters = invocation.Explain?.SummaryParameters
            ?? BuildDefaultSummaryParameters(descriptor, runtimeSummary, invocation.Diagnostics.FirstOrDefault());
        AiExplainValueProvenanceProjection provenance = BuildProvenance(runtimeSummary, sessionDigest, resolvedProviderId, packId, invocation);
        IReadOnlyList<AiExplainEvidencePointerProjection> evidence = BuildEvidence(runtimeSummary, sessionDigest, descriptor, resolvedProviderId, packId, invocation);
        IReadOnlyList<AiExplainTraceStepProjection> trace = BuildTrace(runtimeSummary, sessionDigest, descriptor, resolvedProviderId, packId, invocation);
        AiExplainValueProvenanceEnvelopeProjection provenanceEnvelope = BuildProvenanceEnvelope(
            descriptor,
            resolvedProviderId,
            packId,
            provenance);
        AiExplainEvidenceEnvelopeProjection evidenceEnvelope = BuildEvidenceEnvelope(
            descriptor,
            resolvedProviderId,
            packId,
            evidence);

        return new AiExplainValueProjection(
            ExplainEntryId: explainEntryId,
            Kind: ResolveEntryKind(descriptor),
            TitleKey: RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor),
            TitleParameters: RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor),
            SummaryKey: summaryKey,
            SummaryParameters: summaryParameters,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            RulesetId: runtimeSummary.RulesetId,
            CharacterId: characterDigest?.CharacterId,
            CapabilityId: descriptor.CapabilityId,
            InvocationKind: descriptor.InvocationKind,
            ProviderId: resolvedProviderId,
            PackId: packId,
            Explainable: descriptor.Explainable,
            SessionSafe: descriptor.SessionSafe,
            ProviderGasBudget: descriptor.DefaultGasBudget.ProviderInstructionLimit,
            RequestGasBudget: descriptor.DefaultGasBudget.RequestInstructionLimit,
            Fragments: BuildFragments(runtimeSummary, characterDigest, descriptor, resolvedProviderId, packId, invocation),
            Diagnostics: invocation.Diagnostics,
            Provenance: provenance,
            Trace: trace,
            Evidence: evidence,
            ProvenanceEnvelope: provenanceEnvelope,
            EvidenceEnvelope: evidenceEnvelope);
    }

    private static IReadOnlyList<RulesetCapabilityArgument> BuildInvocationArguments(
        AiRuntimeSummaryProjection runtimeSummary,
        AiCharacterDigestProjection? characterDigest,
        AiExplainValueQuery query)
    {
        List<RulesetCapabilityArgument> arguments =
        [
            new("runtimeFingerprint", RulesetCapabilityBridge.FromObject(runtimeSummary.RuntimeFingerprint)),
            new("rulesetId", RulesetCapabilityBridge.FromObject(runtimeSummary.RulesetId))
        ];

        if (characterDigest is not null)
        {
            arguments.Add(new RulesetCapabilityArgument("characterId", RulesetCapabilityBridge.FromObject(characterDigest.CharacterId)));
            arguments.Add(new RulesetCapabilityArgument("characterName", RulesetCapabilityBridge.FromObject(characterDigest.DisplayName)));
            arguments.Add(new RulesetCapabilityArgument("karma", RulesetCapabilityBridge.FromObject(characterDigest.Summary.Karma)));
        }

        string? explainEntryId = NormalizeOptional(query.ExplainEntryId);
        if (explainEntryId is not null)
        {
            arguments.Add(new RulesetCapabilityArgument("explainEntryId", RulesetCapabilityBridge.FromObject(explainEntryId)));
        }

        return arguments;
    }

    private static IReadOnlyList<AiExplainFragmentProjection> BuildFragments(
        AiRuntimeSummaryProjection runtimeSummary,
        AiCharacterDigestProjection? characterDigest,
        RulesetCapabilityDescriptor descriptor,
        string? providerId,
        string? packId,
        RulesetCapabilityInvocationResult invocation)
    {
        List<AiExplainFragmentProjection> fragments =
        [
            new(
                AiExplainFragmentKinds.Input,
                "ruleset.explain.fragment.runtime",
                [Param("runtimeFingerprint", runtimeSummary.RuntimeFingerprint), Param("rulesetId", runtimeSummary.RulesetId)],
                RulesetCapabilityBridge.FromObject(runtimeSummary.RuntimeFingerprint)),
            new(
                AiExplainFragmentKinds.Constant,
                "ruleset.explain.fragment.capability",
                [Param("capabilityId", descriptor.CapabilityId)],
                RulesetCapabilityBridge.FromObject(descriptor.CapabilityId)),
            new(
                AiExplainFragmentKinds.Constant,
                "ruleset.explain.fragment.invocation",
                [Param("invocationKind", descriptor.InvocationKind)],
                RulesetCapabilityBridge.FromObject(descriptor.InvocationKind))
        ];

        if (characterDigest is not null)
        {
            fragments.Add(new AiExplainFragmentProjection(
                AiExplainFragmentKinds.Input,
                "ruleset.explain.fragment.character",
                [Param("characterId", characterDigest.CharacterId), Param("characterName", characterDigest.DisplayName)],
                RulesetCapabilityBridge.FromObject(characterDigest.CharacterId)));
        }

        if (providerId is not null)
        {
            fragments.Add(new AiExplainFragmentProjection(
                AiExplainFragmentKinds.ProviderStep,
                "ruleset.explain.fragment.provider",
                [Param("providerId", providerId)],
                RulesetCapabilityBridge.FromObject(providerId)));
        }

        if (packId is not null)
        {
            fragments.Add(new AiExplainFragmentProjection(
                AiExplainFragmentKinds.ProviderStep,
                "ruleset.explain.fragment.pack",
                [Param("packId", packId)],
                RulesetCapabilityBridge.FromObject(packId)));
        }

        if (invocation.Explain is not null)
        {
            foreach (RulesetProviderTrace provider in invocation.Explain.Providers)
            {
                fragments.Add(new AiExplainFragmentProjection(
                    AiExplainFragmentKinds.ProviderStep,
                    "ruleset.explain.fragment.provider.gas",
                    [
                        Param("providerId", provider.ProviderId),
                        Param("providerInstructionsConsumed", provider.GasUsage.ProviderInstructionsConsumed),
                        Param("requestInstructionsConsumed", provider.GasUsage.RequestInstructionsConsumed),
                        Param("peakMemoryBytes", provider.GasUsage.PeakMemoryBytes)
                    ],
                    RulesetCapabilityBridge.FromObject(provider.GasUsage.ProviderInstructionsConsumed)));

                foreach (RulesetTraceStep step in provider.Steps)
                {
                    fragments.Add(new AiExplainFragmentProjection(
                        AiExplainFragmentKinds.Note,
                        step.ExplanationKey,
                        step.ExplanationParameters,
                        step.Modifier is decimal modifier
                            ? RulesetCapabilityBridge.FromObject(modifier)
                            : null));
                }
            }
        }
        else
        {
            foreach (KeyValuePair<string, object?> output in RulesetCapabilityBridge.ToOutputDictionary(invocation.Output))
            {
                fragments.Add(new AiExplainFragmentProjection(
                    AiExplainFragmentKinds.Output,
                    "ruleset.explain.fragment.output",
                    [Param("outputKey", output.Key)],
                    RulesetCapabilityBridge.FromObject(output.Value)));
            }

            fragments.Add(new AiExplainFragmentProjection(
                AiExplainFragmentKinds.Note,
                "ruleset.explain.fragment.trace.missing",
                [],
                null));
        }

        foreach (RulesetCapabilityDiagnostic diagnostic in invocation.Diagnostics)
        {
            fragments.Add(new AiExplainFragmentProjection(
                AiExplainFragmentKinds.Warning,
                "ruleset.explain.fragment.diagnostic",
                [Param("code", diagnostic.Code), Param("severity", diagnostic.Severity)],
                RulesetCapabilityBridge.FromObject(diagnostic.Code)));
        }

        return fragments;
    }

    private static AiExplainValueProvenanceProjection BuildProvenance(
        AiRuntimeSummaryProjection runtimeSummary,
        AiSessionDigestProjection? sessionDigest,
        string? providerId,
        string? packId,
        RulesetCapabilityInvocationResult invocation)
    {
        return new AiExplainValueProvenanceProjection(
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            RulesetId: runtimeSummary.RulesetId,
            EngineApiVersion: runtimeSummary.EngineApiVersion,
            CatalogKind: runtimeSummary.CatalogKind,
            RuntimeTitle: runtimeSummary.Title,
            ProfileId: NormalizeOptional(invocation.Explain?.ProfileId) ?? sessionDigest?.ProfileId,
            ProfileTitle: sessionDigest?.ProfileTitle,
            ProviderId: providerId,
            PackId: packId,
            RulePacks: runtimeSummary.RulePacks,
            ProviderBindings: new Dictionary<string, string>(runtimeSummary.ProviderBindings, StringComparer.Ordinal));
    }

    private static AiExplainValueProvenanceEnvelopeProjection BuildProvenanceEnvelope(
        RulesetCapabilityDescriptor descriptor,
        string? providerId,
        string? packId,
        AiExplainValueProvenanceProjection provenance)
    {
        return new AiExplainValueProvenanceEnvelopeProjection(
            Schema: AiExplainEnvelopeSchemas.ProvenanceV1,
            Provenance: provenance,
            CapabilityId: descriptor.CapabilityId,
            ProviderId: providerId,
            PackId: packId);
    }

    private static AiExplainEvidenceEnvelopeProjection BuildEvidenceEnvelope(
        RulesetCapabilityDescriptor descriptor,
        string? providerId,
        string? packId,
        IReadOnlyList<AiExplainEvidencePointerProjection> evidence)
    {
        return new AiExplainEvidenceEnvelopeProjection(
            Schema: AiExplainEnvelopeSchemas.EvidenceV1,
            Pointers: evidence,
            CapabilityId: descriptor.CapabilityId,
            ProviderId: providerId,
            PackId: packId);
    }

    private static IReadOnlyList<AiExplainTraceStepProjection> BuildTrace(
        AiRuntimeSummaryProjection runtimeSummary,
        AiSessionDigestProjection? sessionDigest,
        RulesetCapabilityDescriptor descriptor,
        string? providerId,
        string? packId,
        RulesetCapabilityInvocationResult invocation)
    {
        List<AiExplainTraceStepProjection> steps = [];
        IReadOnlyList<AiExplainEvidencePointerProjection> defaultEvidence = BuildEvidence(
            runtimeSummary,
            sessionDigest,
            descriptor,
            providerId,
            packId,
            invocation);

        if (invocation.Explain is not null)
        {
            int providerIndex = 0;
            foreach (RulesetProviderTrace provider in invocation.Explain.Providers)
            {
                int stepIndex = 0;
                foreach (RulesetTraceStep step in provider.Steps)
                {
                    steps.Add(new AiExplainTraceStepProjection(
                        StepId: $"{provider.ProviderId}:{providerIndex}:{stepIndex}",
                        ProviderId: provider.ProviderId,
                        CapabilityId: step.CapabilityId,
                        PackId: step.PackId,
                        Category: step.Category,
                        ExplanationKey: step.ExplanationKey,
                        ExplanationParameters: step.ExplanationParameters,
                        Modifier: step.Modifier,
                        Certain: step.Certain,
                        RuleId: step.RuleId,
                        Evidence: MergeEvidence(defaultEvidence, ToEvidence(step.Evidence))));
                    stepIndex++;
                }

                providerIndex++;
            }
        }

        if (steps.Count == 0)
        {
            steps.Add(new AiExplainTraceStepProjection(
                StepId: "binding:0",
                ProviderId: providerId ?? descriptor.CapabilityId,
                CapabilityId: descriptor.CapabilityId,
                PackId: packId,
                Category: "provider-binding",
                ExplanationKey: "ruleset.explain.trace.provider-binding",
                ExplanationParameters:
                [
                    Param("capabilityId", descriptor.CapabilityId),
                    Param("providerId", providerId),
                    Param("packId", packId)
                ],
                Evidence: defaultEvidence));

            if (invocation.Explain is null)
            {
                steps.Add(new AiExplainTraceStepProjection(
                    StepId: "trace:missing",
                    ProviderId: providerId ?? descriptor.CapabilityId,
                    CapabilityId: descriptor.CapabilityId,
                    PackId: packId,
                    Category: "trace",
                    ExplanationKey: "ruleset.explain.fragment.trace.missing",
                    ExplanationParameters: [],
                    Evidence: defaultEvidence));
            }
        }

        for (int diagnosticIndex = 0; diagnosticIndex < invocation.Diagnostics.Count; diagnosticIndex++)
        {
            RulesetCapabilityDiagnostic diagnostic = invocation.Diagnostics[diagnosticIndex];
            steps.Add(new AiExplainTraceStepProjection(
                StepId: $"diagnostic:{diagnosticIndex}",
                ProviderId: providerId ?? descriptor.CapabilityId,
                CapabilityId: descriptor.CapabilityId,
                PackId: packId,
                Category: "diagnostic",
                ExplanationKey: RulesetCapabilityDiagnosticLocalization.ResolveMessageKey(diagnostic),
                ExplanationParameters: RulesetCapabilityDiagnosticLocalization.ResolveMessageParameters(diagnostic),
                Evidence: MergeEvidence(
                    defaultEvidence,
                    [
                        new AiExplainEvidencePointerProjection(
                            Kind: RulesetEvidencePointerKinds.Diagnostic,
                            Pointer: diagnostic.Code,
                            LabelKey: "ruleset.explain.evidence.diagnostic",
                            LabelParameters:
                            [
                                Param("code", diagnostic.Code),
                                Param("severity", diagnostic.Severity)
                            ])
                    ])));
        }

        return steps;
    }

    private static IReadOnlyList<AiExplainEvidencePointerProjection> BuildEvidence(
        AiRuntimeSummaryProjection runtimeSummary,
        AiSessionDigestProjection? sessionDigest,
        RulesetCapabilityDescriptor descriptor,
        string? providerId,
        string? packId,
        RulesetCapabilityInvocationResult invocation)
    {
        Dictionary<string, AiExplainEvidencePointerProjection> evidence = new(StringComparer.Ordinal);

        AddEvidence(
            evidence,
            new AiExplainEvidencePointerProjection(
                Kind: RulesetEvidencePointerKinds.RuntimeLock,
                Pointer: runtimeSummary.RuntimeFingerprint,
                LabelKey: "ruleset.explain.evidence.runtime-lock",
                LabelParameters:
                [
                    Param("runtimeFingerprint", runtimeSummary.RuntimeFingerprint),
                    Param("rulesetId", runtimeSummary.RulesetId)
                ]));
        AddEvidence(
            evidence,
            new AiExplainEvidencePointerProjection(
                Kind: RulesetEvidencePointerKinds.CapabilityDescriptor,
                Pointer: descriptor.CapabilityId,
                LabelKey: "ruleset.explain.evidence.capability-descriptor",
                LabelParameters:
                [
                    Param("capabilityId", descriptor.CapabilityId),
                    Param("invocationKind", descriptor.InvocationKind)
                ],
                ProviderId: providerId,
                PackId: packId,
                RuleId: descriptor.CapabilityId));

        if (!string.IsNullOrWhiteSpace(sessionDigest?.ProfileId))
        {
            AddEvidence(
                evidence,
                new AiExplainEvidencePointerProjection(
                    Kind: RulesetEvidencePointerKinds.RuleProfile,
                    Pointer: sessionDigest!.ProfileId!,
                    LabelKey: "ruleset.explain.evidence.rule-profile",
                    LabelParameters:
                    [
                        Param("profileId", sessionDigest.ProfileId),
                        Param("profileTitle", sessionDigest.ProfileTitle)
                    ]));
        }

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            AddEvidence(
                evidence,
                new AiExplainEvidencePointerProjection(
                    Kind: RulesetEvidencePointerKinds.ProviderBinding,
                    Pointer: providerId!,
                    LabelKey: "ruleset.explain.evidence.provider-binding",
                    LabelParameters:
                    [
                        Param("providerId", providerId),
                        Param("capabilityId", descriptor.CapabilityId)
                    ],
                    ProviderId: providerId,
                    PackId: packId,
                    RuleId: descriptor.CapabilityId));
        }

        if (!string.IsNullOrWhiteSpace(packId))
        {
            AddEvidence(
                evidence,
                new AiExplainEvidencePointerProjection(
                    Kind: RulesetEvidencePointerKinds.RulePack,
                    Pointer: packId!,
                    LabelKey: "ruleset.explain.evidence.rulepack",
                    LabelParameters:
                    [
                        Param("packId", packId)
                    ],
                    ProviderId: providerId,
                    PackId: packId));
        }

        if (invocation.Explain is not null)
        {
            foreach (AiExplainEvidencePointerProjection pointer in ToEvidence(invocation.Explain.Evidence))
            {
                AddEvidence(evidence, pointer);
            }

            foreach (RulesetProviderTrace provider in invocation.Explain.Providers)
            {
                foreach (AiExplainEvidencePointerProjection pointer in ToEvidence(provider.Evidence))
                {
                    AddEvidence(evidence, pointer);
                }

                foreach (RulesetTraceStep step in provider.Steps)
                {
                    foreach (AiExplainEvidencePointerProjection pointer in ToEvidence(step.Evidence))
                    {
                        AddEvidence(evidence, pointer);
                    }
                }
            }
        }

        foreach (RulesetCapabilityDiagnostic diagnostic in invocation.Diagnostics)
        {
            AddEvidence(
                evidence,
                new AiExplainEvidencePointerProjection(
                    Kind: RulesetEvidencePointerKinds.Diagnostic,
                    Pointer: diagnostic.Code,
                    LabelKey: "ruleset.explain.evidence.diagnostic",
                    LabelParameters:
                    [
                        Param("code", diagnostic.Code),
                        Param("severity", diagnostic.Severity)
                    ]));
        }

        return SortEvidence(evidence.Values);
    }

    private static string ResolveEntryKind(RulesetCapabilityDescriptor descriptor)
    {
        if (string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal))
        {
            return AiExplainEntryKinds.QuickActionAvailability;
        }

        return descriptor.Explainable
            ? AiExplainEntryKinds.DerivedValue
            : AiExplainEntryKinds.CapabilityDescriptor;
    }

    private static string? ResolveProviderId(string? providerId, RulesetExplainTrace? explain)
    {
        return NormalizeOptional(providerId)
            ?? NormalizeOptional(explain?.Providers.FirstOrDefault()?.ProviderId);
    }

    private static string? ResolvePackId(string? providerId, RulesetExplainTrace? explain, IEnumerable<string> rulePacks)
    {
        string? explainPackId = NormalizeOptional(explain?.Providers.FirstOrDefault()?.PackId);
        if (explainPackId is not null)
        {
            return explainPackId;
        }

        return TryResolvePackId(providerId, rulePacks);
    }

    private static string? TryResolvePackId(string? providerId, IEnumerable<string> rulePacks)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        foreach (string rulePack in rulePacks)
        {
            string packId = rulePack.Split('@', 2)[0];
            if (providerId.StartsWith($"{packId}:", StringComparison.Ordinal)
                || providerId.StartsWith($"{packId}/", StringComparison.Ordinal)
                || string.Equals(providerId, packId, StringComparison.Ordinal))
            {
                return packId;
            }
        }

        return null;
    }

    private static AiSessionDigestProjection? AlignSessionDigest(
        AiSessionDigestProjection? sessionDigest,
        AiRuntimeSummaryProjection runtimeSummary)
    {
        if (sessionDigest is null)
        {
            return null;
        }

        string? sessionRuntimeFingerprint = NormalizeOptional(sessionDigest.RuntimeFingerprint);
        if (sessionRuntimeFingerprint is not null
            && !string.Equals(sessionRuntimeFingerprint, runtimeSummary.RuntimeFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        string? sessionRulesetId = RulesetDefaults.NormalizeOptional(sessionDigest.RulesetId);
        if (sessionRulesetId is not null
            && !string.Equals(sessionRulesetId, runtimeSummary.RulesetId, StringComparison.Ordinal))
        {
            return null;
        }

        return sessionDigest;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static RulesetExplainParameter Param(string name, object? value)
    {
        return new RulesetExplainParameter(name, RulesetCapabilityBridge.FromObject(value));
    }

    private static IReadOnlyList<RulesetExplainParameter> BuildDefaultSummaryParameters(
        RulesetCapabilityDescriptor descriptor,
        AiRuntimeSummaryProjection runtimeSummary,
        RulesetCapabilityDiagnostic? diagnostic)
    {
        List<RulesetExplainParameter> parameters =
        [
            Param("capabilityId", descriptor.CapabilityId),
            Param("runtimeFingerprint", runtimeSummary.RuntimeFingerprint)
        ];
        if (diagnostic is not null)
        {
            parameters.Add(Param("code", diagnostic.Code));
            parameters.Add(Param("severity", diagnostic.Severity));
        }

        return parameters;
    }

    private static IReadOnlyList<AiExplainEvidencePointerProjection> ToEvidence(IReadOnlyList<RulesetEvidencePointer>? evidence)
    {
        if (evidence is null || evidence.Count == 0)
        {
            return [];
        }

        return evidence
            .Select(pointer => new AiExplainEvidencePointerProjection(
                Kind: pointer.Kind,
                Pointer: pointer.Pointer,
                LabelKey: pointer.LabelKey,
                LabelParameters: pointer.LabelParameters ?? [],
                ProviderId: pointer.ProviderId,
                PackId: pointer.PackId,
                RuleId: pointer.RuleId))
            .ToArray();
    }

    private static IReadOnlyList<AiExplainEvidencePointerProjection> MergeEvidence(
        IReadOnlyList<AiExplainEvidencePointerProjection> baseline,
        IReadOnlyList<AiExplainEvidencePointerProjection> additional)
    {
        Dictionary<string, AiExplainEvidencePointerProjection> merged = new(StringComparer.Ordinal);
        foreach (AiExplainEvidencePointerProjection pointer in baseline)
        {
            AddEvidence(merged, pointer);
        }

        foreach (AiExplainEvidencePointerProjection pointer in additional)
        {
            AddEvidence(merged, pointer);
        }

        return SortEvidence(merged.Values);
    }

    private static IReadOnlyList<AiExplainEvidencePointerProjection> SortEvidence(IEnumerable<AiExplainEvidencePointerProjection> evidence)
    {
        return evidence
            .OrderBy(pointer => pointer.Kind, StringComparer.Ordinal)
            .ThenBy(pointer => pointer.Pointer, StringComparer.Ordinal)
            .ThenBy(pointer => pointer.ProviderId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pointer => pointer.PackId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pointer => pointer.RuleId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddEvidence(
        IDictionary<string, AiExplainEvidencePointerProjection> target,
        AiExplainEvidencePointerProjection pointer)
    {
        string key = $"{pointer.Kind}|{pointer.Pointer}|{pointer.ProviderId}|{pointer.PackId}|{pointer.RuleId}";
        target[key] = pointer;
    }
}
