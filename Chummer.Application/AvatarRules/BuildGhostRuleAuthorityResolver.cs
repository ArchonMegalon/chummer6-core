using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

public sealed record BuildGhostRuleReferenceDocumentAuthority(
    string RulesetId,
    string ProfileId,
    string SourceDigest,
    string SourcebookFingerprint,
    string ReferenceDocumentDigest,
    ReadOnlyMemory<byte> ReferenceDocument);

public interface IBuildGhostRuleReferenceDocumentAuthorityProvider
{
    ValueTask<BuildGhostRuleReferenceDocumentAuthority?> CaptureAsync(
        string rulesetId,
        string profileId,
        CancellationToken cancellationToken);
}

public interface IBuildGhostRuleSourceAnchorAuthorityResolver
{
    ValueTask<IReadOnlyList<SourceAnchor>?> ResolveAsync(
        BuildGhostActiveRuleAuthority authority,
        BuildGhostRuleIntentDescriptor intent,
        CancellationToken cancellationToken);
}

public sealed class XmlBuildGhostRuleSourceAnchorAuthorityResolver
    : IBuildGhostRuleSourceAnchorAuthorityResolver
{
    private const int MaximumReferenceDocumentBytes = 4 * 1024 * 1024;
    private readonly IBuildGhostRuleReferenceDocumentAuthorityProvider _documents;

    public XmlBuildGhostRuleSourceAnchorAuthorityResolver(
        IBuildGhostRuleReferenceDocumentAuthorityProvider documents)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    public async ValueTask<IReadOnlyList<SourceAnchor>?> ResolveAsync(
        BuildGhostActiveRuleAuthority authority,
        BuildGhostRuleIntentDescriptor intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(intent);

        BuildGhostRuleReferenceDocumentAuthority? documentAuthority = await _documents
            .CaptureAsync(authority.Binding.RulesetId, authority.Binding.ProfileId, cancellationToken)
            .ConfigureAwait(false);
        if (!AuthorityMatches(documentAuthority, authority.Binding)
            || intent.SourceReferences is null
            || intent.SourceReferences.Count == 0)
        {
            return null;
        }

        byte[] bytes = documentAuthority!.ReferenceDocument.ToArray();
        if (bytes.Length == 0
            || bytes.Length > MaximumReferenceDocumentBytes
            || !IsCanonicalSha256(documentAuthority.ReferenceDocumentDigest)
            || !FixedDigestEquals(documentAuthority.ReferenceDocumentDigest, ComputeDigest(bytes)))
        {
            return null;
        }

        XDocument document;
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumReferenceDocumentBytes
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException
            or InvalidOperationException
            or IOException)
        {
            return null;
        }

        XElement? root = document.Root;
        if (root is null || root.Name != XName.Get("chummer"))
        {
            return null;
        }

        IReadOnlyList<XElement> rules = root
            .Elements("rules")
            .SelectMany(static element => element.Elements("rule"))
            .ToArray();
        var anchors = new List<SourceAnchor>(intent.SourceReferences.Count);
        foreach (BuildGhostRuleSourceReferenceDescriptor reference in intent.SourceReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XElement[] matches = rules
                .Where(rule => string.Equals(
                    ReadSingleValue(rule, "id"),
                    reference.ReferenceId,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1
                || !TryCreateAnchor(authority, reference, matches[0], out SourceAnchor anchor))
            {
                return null;
            }
            anchors.Add(anchor);
        }
        return anchors;
    }

    private static bool AuthorityMatches(
        BuildGhostRuleReferenceDocumentAuthority? document,
        BuildGhostRuleAuthorityBinding active)
        => document is not null
            && string.Equals(document.RulesetId, active.RulesetId, StringComparison.Ordinal)
            && string.Equals(document.ProfileId, active.ProfileId, StringComparison.Ordinal)
            && FixedDigestEquals(document.SourceDigest, active.SourceDigest)
            && FixedDigestEquals(document.SourcebookFingerprint, active.SourcebookFingerprint);

    private static bool TryCreateAnchor(
        BuildGhostActiveRuleAuthority authority,
        BuildGhostRuleSourceReferenceDescriptor reference,
        XElement rule,
        out SourceAnchor anchor)
    {
        anchor = null!;
        string? id = ReadSingleValue(rule, "id");
        string? name = ReadSingleValue(rule, "name");
        string? source = ReadSingleValue(rule, "source");
        string? pageText = ReadSingleValue(rule, "page");
        if (!IsRequired(reference.AnchorId)
            || !IsRequired(reference.Locale)
            || !IsRequired(reference.ReferenceId)
            || !IsRequired(reference.SourcePackRef)
            || reference.ExpectedPage <= 0
            || !IsRequired(reference.ExpectedSectionHint)
            || !string.Equals(id, reference.ReferenceId, StringComparison.Ordinal)
            || !string.Equals(name, reference.ExpectedSectionHint, StringComparison.Ordinal)
            || !string.Equals(source, reference.SourcePackRef, StringComparison.Ordinal)
            || !authority.ActiveSourcebookIds.Contains(source, StringComparer.Ordinal)
            || !int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out int page)
            || page != reference.ExpectedPage)
        {
            return false;
        }

        anchor = new SourceAnchor(
            reference.AnchorId,
            authority.Binding.RulesetId,
            source!,
            reference.Locale,
            page,
            name!,
            id!);
        return true;
    }

    private static string? ReadSingleValue(XElement parent, string localName)
    {
        XElement[] matches = parent.Elements(localName).ToArray();
        return matches.Length == 1 && IsRequired(matches[0].Value)
            ? matches[0].Value
            : null;
    }

    private static string ComputeDigest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool FixedDigestEquals(string? left, string? right)
        => IsCanonicalSha256(left)
            && IsCanonicalSha256(right)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(left!),
                Encoding.ASCII.GetBytes(right!));

    private static bool IsCanonicalSha256(string? value)
        => value is not null
            && value.Length == 71
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsRequired(string? value)
        => !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);
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
            SourceReferences:
            [
                new BuildGhostRuleSourceReferenceDescriptor(
                    AnchorId: "sr5.initiative-score.159",
                    SourcePackRef: "SR5",
                    Locale: "en",
                    ExpectedPage: 159,
                    ExpectedSectionHint: "Initiative Score",
                    ReferenceId: "A5D18354-17D4-4102-9295-03E6D125CB67")
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
    private readonly IBuildGhostRuleSourceAnchorAuthorityResolver _sourceAnchors;

    public DefaultBuildGhostRuleAuthorityResolver(
        IBuildGhostRuleSubjectAuthorityResolver subjectAuthority,
        IBuildGhostRuleIntentCatalog intents,
        IBuildGhostRuleCapabilityInvoker capabilities)
        : this(subjectAuthority, intents, capabilities, UnavailableSourceAnchorAuthorityResolver.Instance)
    {
    }

    public DefaultBuildGhostRuleAuthorityResolver(
        IBuildGhostRuleSubjectAuthorityResolver subjectAuthority,
        IBuildGhostRuleIntentCatalog intents,
        IBuildGhostRuleCapabilityInvoker capabilities,
        IBuildGhostRuleSourceAnchorAuthorityResolver sourceAnchors)
    {
        _subjectAuthority = subjectAuthority ?? throw new ArgumentNullException(nameof(subjectAuthority));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _sourceAnchors = sourceAnchors ?? throw new ArgumentNullException(nameof(sourceAnchors));
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

        IReadOnlyList<SourceAnchor>? sourceAnchors;
        try
        {
            sourceAnchors = await _sourceAnchors.ResolveAsync(active, intent, cancellationToken)
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
                "source-anchor-authority-unavailable",
                active.Binding,
                intent.IntentId,
                intent.RuleId);
        }

        if (!AnchorsAreAuthorized(sourceAnchors, intent, active))
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
            sourceAnchors!.ToArray(),
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
            && IsCanonicalSha256(binding.RuntimeFingerprint)
            && IsCanonicalSha256(binding.SourceDigest)
            && IsCanonicalSha256(binding.SourcebookFingerprint)
            && IsCanonicalSha256(binding.CustomDataFingerprint)
            && IsCanonicalSha256(binding.GmPolicyFingerprint)
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
            && intent.SourceReferences is not null
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
            && intent.SourceReferences.All(static reference => reference is not null
                && IsRequired(reference.AnchorId)
                && IsRequired(reference.SourcePackRef)
                && IsRequired(reference.Locale)
                && reference.ExpectedPage > 0
                && IsRequired(reference.ExpectedSectionHint)
                && IsRequired(reference.ReferenceId))
            && intent.SourceReferences
                .Select(static reference => reference!.AnchorId)
                .Distinct(StringComparer.Ordinal)
                .Count() == intent.SourceReferences.Count
            && intent.SourceReferences
                .Select(static reference => reference!.ReferenceId)
                .Distinct(StringComparer.Ordinal)
                .Count() == intent.SourceReferences.Count
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
        IReadOnlyList<SourceAnchor>? anchors,
        BuildGhostRuleIntentDescriptor intent,
        BuildGhostActiveRuleAuthority active)
        => anchors is not null
            && anchors.Count == intent.SourceReferences.Count
            && anchors.Count > 0
            && anchors.All(static anchor => anchor is not null)
            && anchors
                .Select(static anchor => anchor!.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == anchors.Count
            && anchors
                .Select(static anchor => anchor!.AnchorKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == anchors.Count
            && anchors.Select((anchor, index) => (anchor, reference: intent.SourceReferences[index]))
                .All(pair =>
                {
                    SourceAnchor? anchor = pair.anchor;
                    BuildGhostRuleSourceReferenceDescriptor? reference = pair.reference;
                    return anchor is not null
                        && reference is not null
                        && string.Equals(anchor.Id, reference.AnchorId, StringComparison.Ordinal)
                        && string.Equals(anchor.SourcePackRef, reference.SourcePackRef, StringComparison.Ordinal)
                        && string.Equals(anchor.Locale, reference.Locale, StringComparison.Ordinal)
                        && anchor.Page == reference.ExpectedPage
                        && string.Equals(anchor.SectionHint, reference.ExpectedSectionHint, StringComparison.Ordinal)
                        && string.Equals(anchor.AnchorKey, reference.ReferenceId, StringComparison.Ordinal);
                })
            && anchors.All(anchor =>
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

    private static bool IsCanonicalSha256(string? value)
        => value is not null
            && value.Length == 71
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

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

    private sealed class UnavailableSourceAnchorAuthorityResolver
        : IBuildGhostRuleSourceAnchorAuthorityResolver
    {
        public static readonly UnavailableSourceAnchorAuthorityResolver Instance = new();

        public ValueTask<IReadOnlyList<SourceAnchor>?> ResolveAsync(
            BuildGhostActiveRuleAuthority authority,
            BuildGhostRuleIntentDescriptor intent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<SourceAnchor>?>(null);
        }
    }
}
