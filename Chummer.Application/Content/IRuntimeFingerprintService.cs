using Chummer.Contracts.Content;

namespace Chummer.Application.Content;

public interface IRuntimeFingerprintService
{
    string ComputeResolvedRuntimeFingerprint(
        string rulesetId,
        IReadOnlyList<ContentBundleDescriptor> contentBundles,
        IReadOnlyList<RulePackRegistryEntry> rulePacks,
        IReadOnlyDictionary<string, string> providerBindings,
        string engineApiVersion,
        IReadOnlyDictionary<string, string>? capabilityAbiVersions = null);
}
