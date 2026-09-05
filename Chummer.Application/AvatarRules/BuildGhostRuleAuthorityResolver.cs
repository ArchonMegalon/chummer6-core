using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.AvatarRules;

public interface IBuildGhostRuleSubjectAuthorityResolver
{
    ValueTask<BuildGhostActiveRuleAuthority?> ResolveAsync(
        string ownerId,
        string workspaceId,
        string subjectId,
        CancellationToken cancellationToken);
}

public interface IBuildGhostRuleIntentCatalog
{
    BuildGhostRuleIntentDescriptor? Resolve(
        string rulesetId,
        string profileId,
        string intentId,
        int intentVersion,
        string capabilityId,
        string invocationKind);
}

public interface IBuildGhostRuleCapabilityInvoker
{
    ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(
        BuildGhostActiveRuleAuthority authority,
        RulesetCapabilityInvocationRequest request,
        CancellationToken cancellationToken);
}

public interface IBuildGhostRuleAuthorityResolver
{
    ValueTask<BuildGhostRuleAuthorityResolution> ResolveAsync(
        BuildGhostRuleAuthorityRequest? request,
        CancellationToken cancellationToken);
}

public sealed class DefaultBuildGhostRuleIntentCatalog : IBuildGhostRuleIntentCatalog
{
    private static readonly IReadOnlyList<BuildGhostRuleIntentDescriptor> BuiltInDescriptors =
    [
        new(
            IntentId: BuildGhostRuleIntentIds.DeriveInitiative,
            IntentVersion: 1,
            RulesetId: RulesetDefaults.Sr5,
            ProfileId: "official.sr5.core",
            CapabilityId: RulePackCapabilityIds.DeriveInitiative,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            RuleId: "sr5.derived_stats.initiative_score",
            Arguments:
            [
                new("reaction", RulesetCapabilityValueKinds.Integer, 0, int.MaxValue),
                new("intuition", RulesetCapabilityValueKinds.Integer, 0, int.MaxValue),
                new("initiativeDice", RulesetCapabilityValueKinds.Integer, 0, int.MaxValue)
            ],
            SourceAnchors:
            [
                new SourceAnchor(
                    Id: "sr5.initiative-score.159",
                    RulesetId: RulesetDefaults.Sr5,
                    SourcePackRef: "SR5",
                    Locale: "en",
                    Page: 159,
                    SectionHint: "Initiative Score",
                    AnchorKey: "A5D18354-17D4-4102-9295-03E6D125CB67")
            ])
    ];

    private readonly IReadOnlyList<BuildGhostRuleIntentDescriptor> _descriptors;

    public DefaultBuildGhostRuleIntentCatalog()
        : this(BuiltInDescriptors)
    {
    }

    public DefaultBuildGhostRuleIntentCatalog(IReadOnlyList<BuildGhostRuleIntentDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = descriptors.ToArray();
    }

    public BuildGhostRuleIntentDescriptor? Resolve(
        string rulesetId,
        string profileId,
        string intentId,
        int intentVersion,
        string capabilityId,
        string invocationKind)
        => _descriptors.SingleOrDefault(descriptor =>
            string.Equals(descriptor.RulesetId, rulesetId, StringComparison.Ordinal)
            && string.Equals(descriptor.ProfileId, profileId, StringComparison.Ordinal)
            && string.Equals(descriptor.IntentId, intentId, StringComparison.Ordinal)
            && descriptor.IntentVersion == intentVersion
            && string.Equals(descriptor.CapabilityId, capabilityId, StringComparison.Ordinal)
            && string.Equals(descriptor.InvocationKind, invocationKind, StringComparison.Ordinal));
}

public sealed class RulesetPluginBuildGhostRuleCapabilityInvoker : IBuildGhostRuleCapabilityInvoker
{
    private readonly IRulesetPluginRegistry _plugins;

    public RulesetPluginBuildGhostRuleCapabilityInvoker(IRulesetPluginRegistry plugins)
    {
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
    }

    public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(
        BuildGhostActiveRuleAuthority authority,
        RulesetCapabilityInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(request);

        IRulesetPlugin? plugin = _plugins.Resolve(authority.Binding.RulesetId);
        RulesetCapabilityDescriptor? descriptor = plugin?.CapabilityDescriptors
            .GetCapabilityDescriptors()
            .SingleOrDefault(candidate =>
                string.Equals(candidate.CapabilityId, request.CapabilityId, StringComparison.Ordinal)
                && string.Equals(candidate.InvocationKind, request.InvocationKind, StringComparison.Ordinal));
        if (plugin is null || descriptor is null || !descriptor.Explainable)
        {
            return ValueTask.FromResult(Failure(
                "build-ghost.rule-capability-unavailable",
                "The selected ruleset does not expose this explainable capability."));
        }

        BuildGhostRuleAuthorityBinding binding = authority.Binding;
        RulesetCapabilityInvocationRequest boundRequest = request with
        {
            Options = new RulesetExecutionOptions(Explain: true, request.Options?.GasBudget),
            AuthorityBinding = new RulesetCapabilityAuthorityBinding(
                binding.RulesetId,
                binding.ProfileId,
                binding.RuntimeFingerprint,
                binding.SourceDigest,
                binding.SourcebookFingerprint,
                binding.CustomDataFingerprint,
                binding.GmPolicyFingerprint,
                binding.WorkspaceRevision)
        };
        return plugin.Capabilities.InvokeAsync(boundRequest, cancellationToken);
    }

    private static RulesetCapabilityInvocationResult Failure(string code, string message)
        => new(
            Success: false,
            Output: null,
            Diagnostics:
            [
                new RulesetCapabilityDiagnostic(
                    code,
                    message,
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: code)
            ]);
}

public sealed class DefaultBuildGhostRuleAuthorityResolver : IBuildGhostRuleAuthorityResolver
{
    private readonly IBuildGhostRuleSubjectAuthorityResolver _subjectAuthority;
    private readonly IBuildGhostRuleIntentCatalog _intents;
    private readonly IBuildGhostRuleCapabilityInvoker _capabilities;

    public DefaultBuildGhostRuleAuthorityResolver(
        IBuildGhostRuleSubjectAuthorityResolver subjectAuthority,
        IBuildGhostRuleIntentCatalog intents,
        IBuildGhostRuleCapabilityInvoker capabilities)
    {
        _subjectAuthority = subjectAuthority ?? throw new ArgumentNullException(nameof(subjectAuthority));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public async ValueTask<BuildGhostRuleAuthorityResolution> ResolveAsync(
        BuildGhostRuleAuthorityRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsRequestShapeValid(request))
        {
            return Failure(BuildGhostRuleAuthorityStatuses.Unresolved, "typed-intent-invalid");
        }

        BuildGhostActiveRuleAuthority? active;
        try
        {
            active = await _subjectAuthority.ResolveAsync(
                    request!.OwnerId,
                    request.WorkspaceId,
                    request.SubjectId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(BuildGhostRuleAuthorityStatuses.Unavailable, "subject-authority-unavailable");
        }

        if (active is null || !IsActiveAuthorityValid(active))
        {
            return Failure(BuildGhostRuleAuthorityStatuses.Unresolved, "subject-authority-unresolved");
        }

        if (!BindingsEqual(request!.ExpectedBinding, active.Binding))
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Stale,
                "rule-environment-binding-mismatch",
                active.Binding);
        }

        BuildGhostRuleIntentDescriptor? intent;
        try
        {
            intent = _intents.Resolve(
                active.Binding.RulesetId,
                active.Binding.ProfileId,
                request.IntentId,
                request.IntentVersion,
                request.CapabilityId,
                request.InvocationKind);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unavailable,
                "typed-intent-catalog-unavailable",
                active.Binding);
        }
        if (intent is null)
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unresolved,
                "typed-intent-unsupported",
                active.Binding);
        }

        if (!IntentIsBoundToRequest(intent, active.Binding, request))
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unresolved,
                "typed-intent-invalid",
                active.Binding);
        }

        if (!ArgumentsAreExact(request.Arguments, intent.Arguments))
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unresolved,
                "typed-arguments-invalid",
                active.Binding,
                intent.IntentId,
                intent.RuleId);
        }

        if (!AnchorsAreAuthorized(intent, active))
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unresolved,
                "page-backed-source-anchor-unavailable",
                active.Binding,
                intent.IntentId,
                intent.RuleId);
        }

        RulesetCapabilityInvocationResult invocation;
        try
        {
            invocation = await _capabilities.InvokeAsync(
                    active,
                    new RulesetCapabilityInvocationRequest(
                        intent.CapabilityId,
                        intent.InvocationKind,
                        request.Arguments,
                        new RulesetExecutionOptions(Explain: true),
                        Source: BuildGhostRuleAuthorityContractVersions.RequestV1),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unavailable,
                "capability-invocation-unavailable",
                active.Binding,
                intent.IntentId,
                intent.RuleId);
        }

        if (!InvocationIsGrounded(invocation, active.Binding, intent))
        {
            return Failure(
                BuildGhostRuleAuthorityStatuses.Unresolved,
                "capability-result-ungrounded",
                active.Binding,
                intent.IntentId,
                intent.RuleId);
        }

        return new BuildGhostRuleAuthorityResolution(
            BuildGhostRuleAuthorityStatuses.Resolved,
            intent.IntentId,
            intent.RuleId,
            active.Binding,
            invocation.Output,
            invocation.Explain,
            intent.SourceAnchors.ToArray(),
            UncertaintyReason: null);
    }

    private static bool IsRequestShapeValid(BuildGhostRuleAuthorityRequest? request)
        => request is not null
            && string.Equals(request.ContractVersion, BuildGhostRuleAuthorityContractVersions.RequestV1, StringComparison.Ordinal)
            && IsRequired(request.OwnerId)
            && IsRequired(request.WorkspaceId)
            && IsRequired(request.SubjectId)
            && IsRequired(request.IntentId)
            && request.IntentVersion > 0
            && IsRequired(request.CapabilityId)
            && (string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
                || string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Script, StringComparison.Ordinal))
            && request.Arguments is not null
            && IsBindingValid(request.ExpectedBinding);

    private static bool IsActiveAuthorityValid(BuildGhostActiveRuleAuthority active)
        => IsBindingValid(active.Binding)
            && active.ActiveSourcebookIds is not null
            && active.ActiveSourcebookIds.All(IsRequired)
            && active.ActiveSourcebookIds.Distinct(StringComparer.Ordinal).Count() == active.ActiveSourcebookIds.Count;

    private static bool IsBindingValid(BuildGhostRuleAuthorityBinding? binding)
        => binding is not null
            && IsRequired(binding.RulesetId)
            && string.Equals(RulesetDefaults.NormalizeOptional(binding.RulesetId), binding.RulesetId, StringComparison.Ordinal)
            && IsRequired(binding.ProfileId)
            && IsRequired(binding.RuntimeFingerprint)
            && IsRequired(binding.SourceDigest)
            && IsRequired(binding.SourcebookFingerprint)
            && IsRequired(binding.CustomDataFingerprint)
            && IsRequired(binding.GmPolicyFingerprint)
            && binding.WorkspaceRevision >= 0;

    private static bool BindingsEqual(
        BuildGhostRuleAuthorityBinding expected,
        BuildGhostRuleAuthorityBinding active)
        => expected.WorkspaceRevision == active.WorkspaceRevision
            && string.Equals(expected.RulesetId, active.RulesetId, StringComparison.Ordinal)
            && string.Equals(expected.ProfileId, active.ProfileId, StringComparison.Ordinal)
            && string.Equals(expected.RuntimeFingerprint, active.RuntimeFingerprint, StringComparison.Ordinal)
            && string.Equals(expected.SourceDigest, active.SourceDigest, StringComparison.Ordinal)
            && string.Equals(expected.SourcebookFingerprint, active.SourcebookFingerprint, StringComparison.Ordinal)
            && string.Equals(expected.CustomDataFingerprint, active.CustomDataFingerprint, StringComparison.Ordinal)
            && string.Equals(expected.GmPolicyFingerprint, active.GmPolicyFingerprint, StringComparison.Ordinal);

    private static bool IntentIsBoundToRequest(
        BuildGhostRuleIntentDescriptor intent,
        BuildGhostRuleAuthorityBinding active,
        BuildGhostRuleAuthorityRequest request)
        => IsRequired(intent.IntentId)
            && intent.IntentVersion > 0
            && IsRequired(intent.RulesetId)
            && IsRequired(intent.ProfileId)
            && IsRequired(intent.CapabilityId)
            && IsRequired(intent.InvocationKind)
            && IsRequired(intent.RuleId)
            && intent.Arguments is not null
            && intent.SourceAnchors is not null
            && intent.Arguments.All(static descriptor => descriptor is not null
                && IsRequired(descriptor.Name)
                && string.Equals(
                    descriptor.ValueKind,
                    RulesetCapabilityValueKinds.Integer,
                    StringComparison.Ordinal)
                && (!descriptor.MinimumIntegerValue.HasValue
                    || !descriptor.MaximumIntegerValue.HasValue
                    || descriptor.MinimumIntegerValue.Value <= descriptor.MaximumIntegerValue.Value))
            && intent.Arguments
                .Select(static descriptor => descriptor!.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() == intent.Arguments.Count
            && string.Equals(intent.IntentId, request.IntentId, StringComparison.Ordinal)
            && intent.IntentVersion == request.IntentVersion
            && string.Equals(intent.RulesetId, active.RulesetId, StringComparison.Ordinal)
            && string.Equals(intent.ProfileId, active.ProfileId, StringComparison.Ordinal)
            && string.Equals(intent.CapabilityId, request.CapabilityId, StringComparison.Ordinal)
            && string.Equals(intent.InvocationKind, request.InvocationKind, StringComparison.Ordinal);

    private static bool ArgumentsAreExact(
        IReadOnlyList<RulesetCapabilityArgument> arguments,
        IReadOnlyList<BuildGhostRuleIntentArgumentDescriptor> expected)
    {
        if (arguments.Count != expected.Count)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            RulesetCapabilityArgument? argument = arguments[index];
            BuildGhostRuleIntentArgumentDescriptor? descriptor = expected[index];
            if (argument is null
                || descriptor is null
                || !string.Equals(argument.Name, descriptor.Name, StringComparison.Ordinal)
                || !ValueHasExactShape(argument.Value, descriptor))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValueHasExactShape(
        RulesetCapabilityValue? value,
        BuildGhostRuleIntentArgumentDescriptor descriptor)
    {
        if (value is null
            || !string.Equals(value.Kind, descriptor.ValueKind, StringComparison.Ordinal)
            || value.StringValue is not null
            || value.BooleanValue is not null
            || value.NumberValue is not null
            || value.DecimalValue is not null
            || value.Items is not null
            || value.Properties is not null)
        {
            return false;
        }

        if (string.Equals(descriptor.ValueKind, RulesetCapabilityValueKinds.Integer, StringComparison.Ordinal))
        {
            return value.IntegerValue is long integer
                && (!descriptor.MinimumIntegerValue.HasValue || integer >= descriptor.MinimumIntegerValue.Value)
                && (!descriptor.MaximumIntegerValue.HasValue || integer <= descriptor.MaximumIntegerValue.Value);
        }
        return value.IntegerValue is null;
    }

    private static bool AnchorsAreAuthorized(
        BuildGhostRuleIntentDescriptor intent,
        BuildGhostActiveRuleAuthority active)
        => intent.SourceAnchors.Count > 0
            && intent.SourceAnchors.All(static anchor => anchor is not null)
            && intent.SourceAnchors
                .Select(static anchor => anchor!.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == intent.SourceAnchors.Count
            && intent.SourceAnchors
                .Select(static anchor => anchor!.AnchorKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == intent.SourceAnchors.Count
            && intent.SourceAnchors.All(anchor =>
                anchor is not null
                && IsRequired(anchor.Id)
                && string.Equals(anchor.RulesetId, active.Binding.RulesetId, StringComparison.Ordinal)
                && IsRequired(anchor.SourcePackRef)
                && active.ActiveSourcebookIds.Contains(anchor.SourcePackRef, StringComparer.Ordinal)
                && IsRequired(anchor.Locale)
                && anchor.Page > 0
                && IsRequired(anchor.SectionHint)
                && IsRequired(anchor.AnchorKey)
                && string.Equals(
                    anchor.BindingPolicy,
                    SourceAnchorBindingPolicies.UserLocalFileOnly,
                    StringComparison.Ordinal));

    private static bool InvocationIsGrounded(
        RulesetCapabilityInvocationResult? invocation,
        BuildGhostRuleAuthorityBinding binding,
        BuildGhostRuleIntentDescriptor intent)
    {
        RulesetExplainTrace? explain = invocation?.Explain;
        return invocation is not null
            && invocation.Success
            && invocation.Output is not null
            && invocation.Diagnostics is not null
            && invocation.Diagnostics.All(diagnostic => diagnostic is not null
                && !string.Equals(
                    diagnostic.Severity,
                    RulesetCapabilityDiagnosticSeverities.Error,
                    StringComparison.Ordinal))
            && explain is not null
            && string.Equals(explain.RuntimeFingerprint, binding.RuntimeFingerprint, StringComparison.Ordinal)
            && string.Equals(explain.ProfileId, binding.ProfileId, StringComparison.Ordinal)
            && CapabilityValuesEqual(invocation.Output, explain.FinalValue)
            && explain.Providers is not null
            && explain.Providers.Count > 0
            && explain.Providers.All(provider =>
                provider is not null
                && provider.Success
                && string.Equals(provider.CapabilityId, intent.CapabilityId, StringComparison.Ordinal)
                && provider.Steps is not null
                && provider.Steps.Count > 0
                && provider.Steps.All(step => step is not null
                    && string.Equals(step.CapabilityId, intent.CapabilityId, StringComparison.Ordinal)));
    }

    private static bool CapabilityValuesEqual(RulesetCapabilityValue? left, RulesetCapabilityValue? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null
            || !string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
            || !string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal)
            || left.BooleanValue != right.BooleanValue
            || left.IntegerValue != right.IntegerValue
            || left.NumberValue != right.NumberValue
            || left.DecimalValue != right.DecimalValue)
        {
            return false;
        }

        IReadOnlyList<RulesetCapabilityValue>? leftItems = left.Items;
        IReadOnlyList<RulesetCapabilityValue>? rightItems = right.Items;
        if ((leftItems is null) != (rightItems is null)
            || leftItems is not null && rightItems is not null
            && (leftItems.Count != rightItems.Count
                || leftItems.Where((item, index) => !CapabilityValuesEqual(item, rightItems[index])).Any()))
        {
            return false;
        }

        IReadOnlyDictionary<string, RulesetCapabilityValue>? leftProperties = left.Properties;
        IReadOnlyDictionary<string, RulesetCapabilityValue>? rightProperties = right.Properties;
        return (leftProperties is null) == (rightProperties is null)
            && (leftProperties is null
                || rightProperties is not null
                && leftProperties.Count == rightProperties.Count
                && leftProperties.All(pair =>
                    rightProperties.TryGetValue(pair.Key, out RulesetCapabilityValue? other)
                    && CapabilityValuesEqual(pair.Value, other)));
    }

    private static bool IsRequired(string? value)
        => !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static BuildGhostRuleAuthorityResolution Failure(
        string status,
        string reason,
        BuildGhostRuleAuthorityBinding? activeBinding = null,
        string? intentId = null,
        string? ruleId = null)
        => new(
            status,
            intentId,
            ruleId,
            activeBinding,
            Output: null,
            Explain: null,
            SourceAnchors: [],
            UncertaintyReason: reason);
}
