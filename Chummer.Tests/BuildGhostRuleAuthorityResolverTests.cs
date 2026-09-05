using Chummer.Application.AvatarRules;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Chummer.Tests;

[TestClass]
public sealed class BuildGhostRuleAuthorityResolverTests
{
    [TestMethod]
    public void Existing_ruleset_capability_request_constructor_remains_public_and_unbound()
    {
        Type[] legacySignature =
        [
            typeof(string),
            typeof(string),
            typeof(IReadOnlyList<RulesetCapabilityArgument>),
            typeof(RulesetExecutionOptions),
            typeof(string),
            typeof(string)
        ];

        Assert.IsNotNull(typeof(RulesetCapabilityInvocationRequest).GetConstructor(legacySignature));

        RulesetCapabilityInvocationRequest request = new(
            "legacy.capability",
            RulesetCapabilityInvocationKinds.Rule,
            [],
            new RulesetExecutionOptions(),
            "legacy-provider",
            "legacy-source");
        Assert.IsNull(request.AuthorityBinding);
    }

    [TestMethod]
    public async Task Existing_unbound_sr5_requests_keep_their_legacy_trace_binding()
    {
        Sr5RulesetPlugin plugin = new();
        RulesetCapabilityInvocationResult result = await plugin.Capabilities.InvokeAsync(
            new RulesetCapabilityInvocationRequest(
                RulePackCapabilityIds.DeriveInitiative,
                RulesetCapabilityInvocationKinds.Rule,
                [
                    new("reaction", RulesetCapabilityBridge.FromObject(6)),
                    new("intuition", RulesetCapabilityBridge.FromObject(5)),
                    new("initiativeDice", RulesetCapabilityBridge.FromObject(4))
                ],
                new RulesetExecutionOptions(Explain: true)),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Explain?.RuntimeFingerprint);
        Assert.AreEqual("official.sr5.core", result.Explain?.ProfileId);
    }

    [TestMethod]
    public void Sr5_initiative_anchor_matches_authoritative_reference_row()
    {
        string referencesPath = FindRepositoryFile("Chummer", "data", "references.xml");
        XElement reference = XDocument.Load(referencesPath)
            .Descendants("rule")
            .Single(rule => string.Equals(
                (string?)rule.Element("id"),
                "A5D18354-17D4-4102-9295-03E6D125CB67",
                StringComparison.Ordinal));

        Assert.AreEqual("Initiative Score", (string?)reference.Element("name"));
        Assert.AreEqual("SR5", (string?)reference.Element("source"));
        Assert.AreEqual("159", (string?)reference.Element("page"));
    }

    [TestMethod]
    public async Task Sr5_initiative_intent_resolves_through_bound_host_with_page_anchor()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        DefaultBuildGhostRuleAuthorityResolver resolver = CreateResolver(authority);

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Resolved, result.Status);
        Assert.AreEqual(BuildGhostRuleIntentIds.DeriveInitiative, result.IntentId);
        Assert.AreEqual("sr5.derived_stats.initiative_score", result.RuleId);
        Assert.AreEqual(15L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual(authority.Binding.RuntimeFingerprint, result.Explain?.RuntimeFingerprint);
        Assert.AreEqual(authority.Binding.ProfileId, result.Explain?.ProfileId);
        Assert.HasCount(1, result.SourceAnchors);
        Assert.AreEqual("SR5", result.SourceAnchors[0].SourcePackRef);
        Assert.AreEqual(159, result.SourceAnchors[0].Page);
        Assert.AreEqual("A5D18354-17D4-4102-9295-03E6D125CB67", result.SourceAnchors[0].AnchorKey);
        Assert.IsNull(result.UncertaintyReason);
    }

    [TestMethod]
    public async Task Ruleset_or_profile_without_exact_intent_remains_unresolved()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority(
            rulesetId: RulesetDefaults.Sr6,
            profileId: "official.sr6.core",
            activeSourcebookIds: ["SR6"]);
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("typed-intent-unsupported", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Stale_expected_binding_fails_before_capability_invocation()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));
        BuildGhostRuleAuthorityBinding stale = authority.Binding with { WorkspaceRevision = 416 };

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(stale),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Stale, result.Status);
        Assert.AreEqual("rule-environment-binding-mismatch", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.AreEqual(authority.Binding, result.ActiveBinding);
    }

    [TestMethod]
    [DataRow("ruleset")]
    [DataRow("profile")]
    [DataRow("runtime")]
    [DataRow("source")]
    [DataRow("sourcebooks")]
    [DataRow("custom-data")]
    [DataRow("gm-policy")]
    [DataRow("revision")]
    public async Task Every_expected_authority_domain_is_compared_exactly(string changedDomain)
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));
        BuildGhostRuleAuthorityBinding stale = changedDomain switch
        {
            "ruleset" => authority.Binding with { RulesetId = RulesetDefaults.Sr6 },
            "profile" => authority.Binding with { ProfileId = "official.sr5.alternate" },
            "runtime" => authority.Binding with { RuntimeFingerprint = Digest('f') },
            "source" => authority.Binding with { SourceDigest = Digest('f') },
            "sourcebooks" => authority.Binding with { SourcebookFingerprint = Digest('f') },
            "custom-data" => authority.Binding with { CustomDataFingerprint = Digest('f') },
            "gm-policy" => authority.Binding with { GmPolicyFingerprint = Digest('f') },
            "revision" => authority.Binding with { WorkspaceRevision = 416 },
            _ => throw new AssertFailedException($"Unknown binding domain: {changedDomain}")
        };

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(stale),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Stale, result.Status);
        Assert.AreEqual("rule-environment-binding-mismatch", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsNull(result.Output);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task String_coercion_and_argument_reordering_are_rejected()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));
        BuildGhostRuleAuthorityRequest request = CreateRequest(authority.Binding) with
        {
            Arguments =
            [
                new RulesetCapabilityArgument("intuition", RulesetCapabilityBridge.FromObject(5)),
                new RulesetCapabilityArgument("reaction", RulesetCapabilityBridge.FromObject("6")),
                new RulesetCapabilityArgument("initiativeDice", RulesetCapabilityBridge.FromObject(4))
            ]
        };

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            request,
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("typed-arguments-invalid", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
    }

    [TestMethod]
    public async Task Missing_duplicate_extra_and_out_of_range_arguments_are_rejected()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        IReadOnlyList<IReadOnlyList<RulesetCapabilityArgument>> invalidArgumentSets =
        [
            CreateRequest(authority.Binding).Arguments.Take(2).ToArray(),
            [
                new("reaction", RulesetCapabilityBridge.FromObject(6)),
                new("reaction", RulesetCapabilityBridge.FromObject(5)),
                new("initiativeDice", RulesetCapabilityBridge.FromObject(4))
            ],
            [
                .. CreateRequest(authority.Binding).Arguments,
                new("edge", RulesetCapabilityBridge.FromObject(1))
            ],
            [
                new("reaction", RulesetCapabilityBridge.FromObject(-1)),
                new("intuition", RulesetCapabilityBridge.FromObject(5)),
                new("initiativeDice", RulesetCapabilityBridge.FromObject(4))
            ]
        ];

        foreach (IReadOnlyList<RulesetCapabilityArgument> arguments in invalidArgumentSets)
        {
            RecordingInvoker invoker = new();
            DefaultBuildGhostRuleAuthorityResolver resolver = new(
                new FixedSubjectAuthorityResolver(authority),
                new DefaultBuildGhostRuleIntentCatalog(),
                invoker);
            BuildGhostRuleAuthorityRequest request = CreateRequest(authority.Binding) with { Arguments = arguments };

            BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(request, CancellationToken.None);

            Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
            Assert.AreEqual("typed-arguments-invalid", result.UncertaintyReason);
            Assert.AreEqual(0, invoker.InvocationCount);
            Assert.IsNull(result.Output);
        }
    }

    [TestMethod]
    public async Task Inactive_sourcebook_fails_closed_without_returning_anchor()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority(activeSourcebookIds: ["RUN_FASTER"]);
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Active_sourcebook_matching_is_ordinal_and_case_sensitive()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority(activeSourcebookIds: ["sr5"]);
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Static_intent_metadata_cannot_resolve_without_runtime_source_authority()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker);

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Missing_or_digest_tampered_reference_document_fails_closed()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        IBuildGhostRuleSourceAnchorAuthorityResolver[] sourceResolvers =
        [
            new XmlBuildGhostRuleSourceAnchorAuthorityResolver(
                new FixedReferenceDocumentAuthorityProvider(null)),
            CreateSourceAnchorResolver(
                authority,
                referenceDocument: ReferenceDocument(),
                referenceDocumentDigest: Digest('f'))
        ];

        foreach (IBuildGhostRuleSourceAnchorAuthorityResolver sourceResolver in sourceResolvers)
        {
            RecordingInvoker invoker = new();
            DefaultBuildGhostRuleAuthorityResolver resolver = new(
                new FixedSubjectAuthorityResolver(authority),
                new DefaultBuildGhostRuleIntentCatalog(),
                invoker,
                sourceResolver);

            BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
                CreateRequest(authority.Binding),
                CancellationToken.None);

            Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
            Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
            Assert.AreEqual(0, invoker.InvocationCount);
            Assert.IsEmpty(result.SourceAnchors);
        }
    }

    [TestMethod]
    public async Task Tampered_reference_row_fails_even_when_its_byte_digest_is_current()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        byte[][] tamperedDocuments =
        [
            ReferenceDocument(page: "160"),
            ReferenceDocument(name: "Initiative Guess"),
            ReferenceDocument(source: "RUN_FASTER"),
            ReferenceDocument(referenceId: "00000000-0000-0000-0000-000000000000")
        ];

        foreach (byte[] document in tamperedDocuments)
        {
            RecordingInvoker invoker = new();
            DefaultBuildGhostRuleAuthorityResolver resolver = new(
                new FixedSubjectAuthorityResolver(authority),
                new DefaultBuildGhostRuleIntentCatalog(),
                invoker,
                CreateSourceAnchorResolver(authority, document));

            BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
                CreateRequest(authority.Binding),
                CancellationToken.None);

            Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
            Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
            Assert.AreEqual(0, invoker.InvocationCount);
            Assert.IsEmpty(result.SourceAnchors);
        }
    }

    [TestMethod]
    public async Task Reference_document_must_match_both_active_content_bindings()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        IBuildGhostRuleSourceAnchorAuthorityResolver[] mismatchedResolvers =
        [
            CreateSourceAnchorResolver(authority, sourceDigest: Digest('f')),
            CreateSourceAnchorResolver(authority, sourcebookFingerprint: Digest('f'))
        ];

        foreach (IBuildGhostRuleSourceAnchorAuthorityResolver sourceResolver in mismatchedResolvers)
        {
            RecordingInvoker invoker = new();
            DefaultBuildGhostRuleAuthorityResolver resolver = new(
                new FixedSubjectAuthorityResolver(authority),
                new DefaultBuildGhostRuleIntentCatalog(),
                invoker,
                sourceResolver);

            BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
                CreateRequest(authority.Binding),
                CancellationToken.None);

            Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
            Assert.AreEqual("page-backed-source-anchor-unavailable", result.UncertaintyReason);
            Assert.AreEqual(0, invoker.InvocationCount);
            Assert.IsEmpty(result.SourceAnchors);
        }
    }

    [TestMethod]
    public async Task Blank_or_mismatched_trace_binding_is_rejected()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RulesetCapabilityValue output = RulesetCapabilityBridge.FromObject(15);
        RulesetGasUsage gas = new(1, 1, 1);
        RulesetExplainTrace unboundTrace = new(
            TargetKey: "initiative.total",
            FinalValue: output,
            SummaryKey: "test",
            SummaryParameters: [],
            Providers:
            [
                new RulesetProviderTrace(
                    "sr5.host/derive.initiative",
                    RulePackCapabilityIds.DeriveInitiative,
                    "official.sr5.core",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            "sr5.host/derive.initiative",
                            RulePackCapabilityIds.DeriveInitiative,
                            "official.sr5.core",
                            "test",
                            [],
                            "deterministic-host")
                    ],
                    gas)
            ],
            AggregateGasUsage: gas,
            RuntimeFingerprint: null,
            ProfileId: authority.Binding.ProfileId);
        RecordingInvoker invoker = new(new RulesetCapabilityInvocationResult(true, output, [], unboundTrace));
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("capability-result-ungrounded", result.UncertaintyReason);
        Assert.AreEqual(1, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Capability_output_must_equal_the_explain_trace_final_value()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        RulesetCapabilityValue output = RulesetCapabilityBridge.FromObject(15);
        RulesetCapabilityValue differentFinalValue = RulesetCapabilityBridge.FromObject(14);
        RecordingInvoker invoker = new(new RulesetCapabilityInvocationResult(
            true,
            output,
            [],
            CreateGroundedTrace(authority.Binding, differentFinalValue)));
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            invoker,
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("capability-result-ungrounded", result.UncertaintyReason);
        Assert.AreEqual(1, invoker.InvocationCount);
        Assert.IsNull(result.Output);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Catalog_result_that_is_not_bound_to_the_request_is_rejected()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        BuildGhostRuleIntentDescriptor descriptor = new DefaultBuildGhostRuleIntentCatalog().Resolve(
            authority.Binding.RulesetId,
            authority.Binding.ProfileId,
            BuildGhostRuleIntentIds.DeriveInitiative,
            1,
            RulePackCapabilityIds.DeriveInitiative,
            RulesetCapabilityInvocationKinds.Rule)!;
        RecordingInvoker invoker = new();
        DefaultBuildGhostRuleAuthorityResolver resolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new FixedIntentCatalog(descriptor with { RuleId = "" }),
            invoker);

        BuildGhostRuleAuthorityResolution result = await resolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unresolved, result.Status);
        Assert.AreEqual("typed-intent-invalid", result.UncertaintyReason);
        Assert.AreEqual(0, invoker.InvocationCount);
        Assert.IsEmpty(result.SourceAnchors);
    }

    [TestMethod]
    public async Task Subject_catalog_and_capability_exceptions_fail_closed()
    {
        BuildGhostActiveRuleAuthority authority = CreateAuthority();
        DefaultBuildGhostRuleAuthorityResolver subjectFailureResolver = new(
            new ThrowingSubjectAuthorityResolver(),
            new DefaultBuildGhostRuleIntentCatalog(),
            new RecordingInvoker());

        BuildGhostRuleAuthorityResolution subjectFailure = await subjectFailureResolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unavailable, subjectFailure.Status);
        Assert.AreEqual("subject-authority-unavailable", subjectFailure.UncertaintyReason);
        Assert.IsEmpty(subjectFailure.SourceAnchors);

        DefaultBuildGhostRuleAuthorityResolver catalogFailureResolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new ThrowingIntentCatalog(),
            new RecordingInvoker());

        BuildGhostRuleAuthorityResolution catalogFailure = await catalogFailureResolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unavailable, catalogFailure.Status);
        Assert.AreEqual("typed-intent-catalog-unavailable", catalogFailure.UncertaintyReason);
        Assert.IsEmpty(catalogFailure.SourceAnchors);

        DefaultBuildGhostRuleAuthorityResolver invocationFailureResolver = new(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            new ThrowingInvoker(),
            CreateSourceAnchorResolver(authority));

        BuildGhostRuleAuthorityResolution invocationFailure = await invocationFailureResolver.ResolveAsync(
            CreateRequest(authority.Binding),
            CancellationToken.None);

        Assert.AreEqual(BuildGhostRuleAuthorityStatuses.Unavailable, invocationFailure.Status);
        Assert.AreEqual("capability-invocation-unavailable", invocationFailure.UncertaintyReason);
        Assert.IsEmpty(invocationFailure.SourceAnchors);
    }

    private static RulesetExplainTrace CreateGroundedTrace(
        BuildGhostRuleAuthorityBinding binding,
        RulesetCapabilityValue finalValue)
    {
        RulesetGasUsage gas = new(1, 1, 1);
        return new RulesetExplainTrace(
            TargetKey: "initiative.total",
            FinalValue: finalValue,
            SummaryKey: "test",
            SummaryParameters: [],
            Providers:
            [
                new RulesetProviderTrace(
                    "sr5.host/derive.initiative",
                    RulePackCapabilityIds.DeriveInitiative,
                    "official.sr5.core",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            "sr5.host/derive.initiative",
                            RulePackCapabilityIds.DeriveInitiative,
                            "official.sr5.core",
                            "test",
                            [],
                            "deterministic-host")
                    ],
                    gas)
            ],
            AggregateGasUsage: gas,
            RuntimeFingerprint: binding.RuntimeFingerprint,
            ProfileId: binding.ProfileId);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Repository file not found: {Path.Combine(segments)}");
    }

    private static DefaultBuildGhostRuleAuthorityResolver CreateResolver(BuildGhostActiveRuleAuthority authority)
    {
        RulesetPluginRegistry plugins = new([new Sr5RulesetPlugin()]);
        return new DefaultBuildGhostRuleAuthorityResolver(
            new FixedSubjectAuthorityResolver(authority),
            new DefaultBuildGhostRuleIntentCatalog(),
            new RulesetPluginBuildGhostRuleCapabilityInvoker(plugins),
            CreateSourceAnchorResolver(authority));
    }

    private static XmlBuildGhostRuleSourceAnchorAuthorityResolver CreateSourceAnchorResolver(
        BuildGhostActiveRuleAuthority authority,
        byte[]? referenceDocument = null,
        string? referenceDocumentDigest = null,
        string? sourceDigest = null,
        string? sourcebookFingerprint = null)
    {
        byte[] bytes = referenceDocument ?? File.ReadAllBytes(
            FindRepositoryFile("Chummer", "data", "references.xml"));
        BuildGhostRuleReferenceDocumentAuthority document = new(
            authority.Binding.RulesetId,
            authority.Binding.ProfileId,
            sourceDigest ?? authority.Binding.SourceDigest,
            sourcebookFingerprint ?? authority.Binding.SourcebookFingerprint,
            referenceDocumentDigest ?? ComputeDigest(bytes),
            bytes);
        return new XmlBuildGhostRuleSourceAnchorAuthorityResolver(
            new FixedReferenceDocumentAuthorityProvider(document));
    }

    private static BuildGhostActiveRuleAuthority CreateAuthority(
        string rulesetId = RulesetDefaults.Sr5,
        string profileId = "official.sr5.core",
        IReadOnlyList<string>? activeSourcebookIds = null)
        => new(
            new BuildGhostRuleAuthorityBinding(
                rulesetId,
                profileId,
                RuntimeFingerprint: Digest('a'),
                SourceDigest: Digest('b'),
                SourcebookFingerprint: Digest('c'),
                CustomDataFingerprint: Digest('d'),
                GmPolicyFingerprint: Digest('e'),
                WorkspaceRevision: 417),
            activeSourcebookIds ?? ["SR5"]);

    private static string ComputeDigest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Digest(char value)
        => "sha256:" + new string(value, 64);

    private static byte[] ReferenceDocument(
        string referenceId = "A5D18354-17D4-4102-9295-03E6D125CB67",
        string name = "Initiative Score",
        string source = "SR5",
        string page = "159")
        => Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="utf-8"?>
            <chummer>
              <rules>
                <rule>
                  <id>{{referenceId}}</id>
                  <name>{{name}}</name>
                  <source>{{source}}</source>
                  <page>{{page}}</page>
                </rule>
              </rules>
            </chummer>
            """);

    private static BuildGhostRuleAuthorityRequest CreateRequest(BuildGhostRuleAuthorityBinding binding)
        => new(
            BuildGhostRuleAuthorityContractVersions.RequestV1,
            OwnerId: "owner-1",
            WorkspaceId: "workspace-1",
            SubjectId: "character-1",
            IntentId: BuildGhostRuleIntentIds.DeriveInitiative,
            IntentVersion: 1,
            CapabilityId: RulePackCapabilityIds.DeriveInitiative,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            Arguments:
            [
                new RulesetCapabilityArgument("reaction", RulesetCapabilityBridge.FromObject(6)),
                new RulesetCapabilityArgument("intuition", RulesetCapabilityBridge.FromObject(5)),
                new RulesetCapabilityArgument("initiativeDice", RulesetCapabilityBridge.FromObject(4))
            ],
            ExpectedBinding: binding);

    private sealed class FixedSubjectAuthorityResolver(BuildGhostActiveRuleAuthority? authority)
        : IBuildGhostRuleSubjectAuthorityResolver
    {
        public ValueTask<BuildGhostActiveRuleAuthority?> ResolveAsync(
            string ownerId,
            string workspaceId,
            string subjectId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(authority);
        }
    }

    private sealed class ThrowingSubjectAuthorityResolver : IBuildGhostRuleSubjectAuthorityResolver
    {
        public ValueTask<BuildGhostActiveRuleAuthority?> ResolveAsync(
            string ownerId,
            string workspaceId,
            string subjectId,
            CancellationToken cancellationToken)
            => throw new ArithmeticException("hostile subject authority resolver");
    }

    private sealed class FixedReferenceDocumentAuthorityProvider(
        BuildGhostRuleReferenceDocumentAuthority? document)
        : IBuildGhostRuleReferenceDocumentAuthorityProvider
    {
        public ValueTask<BuildGhostRuleReferenceDocumentAuthority?> CaptureAsync(
            string rulesetId,
            string profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(document);
        }
    }

    private sealed class FixedIntentCatalog(BuildGhostRuleIntentDescriptor descriptor) : IBuildGhostRuleIntentCatalog
    {
        public BuildGhostRuleIntentDescriptor? Resolve(
            string rulesetId,
            string profileId,
            string intentId,
            int intentVersion,
            string capabilityId,
            string invocationKind)
            => descriptor;
    }

    private sealed class ThrowingIntentCatalog : IBuildGhostRuleIntentCatalog
    {
        public BuildGhostRuleIntentDescriptor? Resolve(
            string rulesetId,
            string profileId,
            string intentId,
            int intentVersion,
            string capabilityId,
            string invocationKind)
            => throw new ArithmeticException("hostile intent catalog");
    }

    private sealed class RecordingInvoker(RulesetCapabilityInvocationResult? result = null)
        : IBuildGhostRuleCapabilityInvoker
    {
        public int InvocationCount { get; private set; }

        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(
            BuildGhostActiveRuleAuthority authority,
            RulesetCapabilityInvocationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            return ValueTask.FromResult(result ?? new RulesetCapabilityInvocationResult(
                Success: false,
                Output: null,
                Diagnostics: []));
        }
    }

    private sealed class ThrowingInvoker : IBuildGhostRuleCapabilityInvoker
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(
            BuildGhostActiveRuleAuthority authority,
            RulesetCapabilityInvocationRequest request,
            CancellationToken cancellationToken)
            => throw new ArithmeticException("hostile capability invoker");
    }
}
