using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuntimeLockDiffService : IRuntimeLockDiffService
{
    private static readonly IReadOnlyDictionary<string, int> ChangeKindOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [RuntimeLockDiffChangeKinds.RulesetChanged] = 0,
        [RuntimeLockDiffChangeKinds.EngineApiChanged] = 1,
        [RuntimeLockDiffChangeKinds.ContentBundleAdded] = 2,
        [RuntimeLockDiffChangeKinds.ContentBundleRemoved] = 3,
        [RuntimeLockDiffChangeKinds.RulePackAdded] = 4,
        [RuntimeLockDiffChangeKinds.RulePackRemoved] = 5,
        [RuntimeLockDiffChangeKinds.ProviderBindingChanged] = 6
    };

    public RuntimeLockDiffProjection Diff(ResolvedRuntimeLock before, ResolvedRuntimeLock after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        List<RuntimeLockDiffChange> changes = [];

        if (!string.Equals(before.RulesetId, after.RulesetId, StringComparison.Ordinal))
        {
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.RulesetChanged,
                "ruleset",
                before.RulesetId,
                after.RulesetId,
                "runtime.diff.ruleset.changed",
                [
                    Param("beforeRulesetId", before.RulesetId),
                    Param("afterRulesetId", after.RulesetId)
                ]));
        }

        if (!string.Equals(before.EngineApiVersion, after.EngineApiVersion, StringComparison.Ordinal))
        {
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.EngineApiChanged,
                "engine-api",
                before.EngineApiVersion,
                after.EngineApiVersion,
                "runtime.diff.engine-api.changed",
                [
                    Param("beforeEngineApiVersion", before.EngineApiVersion),
                    Param("afterEngineApiVersion", after.EngineApiVersion)
                ]));
        }

        AppendBundleDiffs(changes, before, after);
        AppendRulePackDiffs(changes, before, after);
        AppendProviderBindingDiffs(changes, before, after);

        RuntimeLockDiffChange[] orderedChanges = changes
            .OrderBy(change => ChangeKindOrder.GetValueOrDefault(change.Kind, int.MaxValue))
            .ThenBy(change => change.SubjectId, StringComparer.Ordinal)
            .ThenBy(change => change.BeforeValue ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(change => change.AfterValue ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        return new RuntimeLockDiffProjection(before.RuntimeFingerprint, after.RuntimeFingerprint, orderedChanges);
    }

    private static void AppendBundleDiffs(List<RuntimeLockDiffChange> changes, ResolvedRuntimeLock before, ResolvedRuntimeLock after)
    {
        IReadOnlyDictionary<string, ContentBundleDescriptor> beforeBundles = before.ContentBundles
            .ToDictionary(CreateBundleKey, static bundle => bundle, StringComparer.Ordinal);
        IReadOnlyDictionary<string, ContentBundleDescriptor> afterBundles = after.ContentBundles
            .ToDictionary(CreateBundleKey, static bundle => bundle, StringComparer.Ordinal);

        foreach (string added in afterBundles.Keys.Except(beforeBundles.Keys).OrderBy(static value => value, StringComparer.Ordinal))
        {
            ContentBundleDescriptor bundle = afterBundles[added];
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.ContentBundleAdded,
                added,
                null,
                added,
                "runtime.diff.content-bundle.added",
                [
                    Param("bundleId", bundle.BundleId),
                    Param("version", bundle.Version),
                    Param("rulesetId", bundle.RulesetId)
                ]));
        }

        foreach (string removed in beforeBundles.Keys.Except(afterBundles.Keys).OrderBy(static value => value, StringComparer.Ordinal))
        {
            ContentBundleDescriptor bundle = beforeBundles[removed];
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.ContentBundleRemoved,
                removed,
                removed,
                null,
                "runtime.diff.content-bundle.removed",
                [
                    Param("bundleId", bundle.BundleId),
                    Param("version", bundle.Version),
                    Param("rulesetId", bundle.RulesetId)
                ]));
        }
    }

    private static void AppendRulePackDiffs(List<RuntimeLockDiffChange> changes, ResolvedRuntimeLock before, ResolvedRuntimeLock after)
    {
        IReadOnlyDictionary<string, ArtifactVersionReference> beforePacks = before.RulePacks
            .ToDictionary(CreateRulePackKey, static pack => pack, StringComparer.Ordinal);
        IReadOnlyDictionary<string, ArtifactVersionReference> afterPacks = after.RulePacks
            .ToDictionary(CreateRulePackKey, static pack => pack, StringComparer.Ordinal);

        foreach (string added in afterPacks.Keys.Except(beforePacks.Keys).OrderBy(static value => value, StringComparer.Ordinal))
        {
            ArtifactVersionReference pack = afterPacks[added];
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.RulePackAdded,
                added,
                null,
                added,
                "runtime.diff.rulepack.added",
                [
                    Param("packId", pack.Id),
                    Param("version", pack.Version)
                ]));
        }

        foreach (string removed in beforePacks.Keys.Except(afterPacks.Keys).OrderBy(static value => value, StringComparer.Ordinal))
        {
            ArtifactVersionReference pack = beforePacks[removed];
            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.RulePackRemoved,
                removed,
                removed,
                null,
                "runtime.diff.rulepack.removed",
                [
                    Param("packId", pack.Id),
                    Param("version", pack.Version)
                ]));
        }
    }

    private static void AppendProviderBindingDiffs(List<RuntimeLockDiffChange> changes, ResolvedRuntimeLock before, ResolvedRuntimeLock after)
    {
        HashSet<string> keys = before.ProviderBindings.Keys
            .Concat(after.ProviderBindings.Keys)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in keys.OrderBy(static candidate => candidate, StringComparer.Ordinal))
        {
            string? beforeBinding = before.ProviderBindings.GetValueOrDefault(key);
            string? afterBinding = after.ProviderBindings.GetValueOrDefault(key);
            if (string.Equals(beforeBinding, afterBinding, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new RuntimeLockDiffChange(
                RuntimeLockDiffChangeKinds.ProviderBindingChanged,
                key,
                beforeBinding,
                afterBinding,
                "runtime.diff.provider-binding.changed",
                [
                    Param("capabilityId", key),
                    Param("beforeProviderId", beforeBinding),
                    Param("afterProviderId", afterBinding)
                ]));
        }
    }

    private static string CreateBundleKey(ContentBundleDescriptor bundle)
        => $"{bundle.BundleId}@{bundle.Version}";

    private static string CreateRulePackKey(ArtifactVersionReference pack)
        => $"{pack.Id}@{pack.Version}";

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
