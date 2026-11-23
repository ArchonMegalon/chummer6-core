using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Issued in-process observation, not a commit permit. DTO copies have no issuer identity.</summary>
public sealed class CharacterCurrentRuleEffectComparison
{
    internal CharacterCurrentRuleEffectComparison(CharacterCurrentRuleEnvironmentExecution beforeIssuer,
        CharacterCurrentRuleEnvironmentExecution afterIssuer, CharacterRuleEffectProjection before,
        CharacterRuleEffectProjection after, IReadOnlyList<string> sourceChanges)
    {
        BeforeIssuer = beforeIssuer; AfterIssuer = afterIssuer;
        Before = before; After = after;
        SourceChanges = Array.AsReadOnly(sourceChanges.ToArray());
        var changes = new List<CharacterRuleEffectChange>();
        foreach (var oldSection in before.Sections)
        {
            var newSection = after.Sections.Single(s => s.Section == oldSection.Section);
            var oldEntities = oldSection.Values.GroupBy(v => v.Entity).ToDictionary(g => g.Key, g => g.ToArray());
            var newEntities = newSection.Values.GroupBy(v => v.Entity).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (string entity in oldEntities.Keys.Union(newEntities.Keys).Order(StringComparer.Ordinal))
            {
                var oldRows = oldEntities.GetValueOrDefault(entity) ?? [];
                var newRows = newEntities.GetValueOrDefault(entity) ?? [];
                var oldInstances = oldRows.GroupBy(v => v.Occurrence).Select(g => g.ToArray()).ToList();
                var newInstances = newRows.GroupBy(v => v.Occurrence).Select(g => g.ToArray()).ToList();
                if (oldInstances.Count <= 1 && newInstances.Count <= 1)
                {
                    var oldValues = oldRows.ToDictionary(Key);
                    var newValues = newRows.ToDictionary(Key);
                    foreach (var key in oldValues.Keys.Union(newValues.Keys)
                        .OrderBy(k => k.Metric).ThenBy(k => k.Qualifier, StringComparer.Ordinal))
                    {
                        oldValues.TryGetValue(key, out var oldValue);
                        newValues.TryGetValue(key, out var newValue);
                        if (!SamePayload(oldValue, newValue))
                            changes.Add(new(oldSection.Section, oldValue, newValue, oldSection.Coverage, newSection.Coverage));
                    }
                    continue;
                }
                // The canonical sheet gives no source identity within this semantic group.
                // Cancel equal complete occurrences; retain every unmatched row, without
                // pairing arbitrary ordinals or pretending that a duplicate has a known delta.
                var remaining = new Dictionary<CharacterRuleEffectValue[], int>(new InstanceComparer());
                foreach (var rows in newInstances)
                    remaining[rows] = remaining.GetValueOrDefault(rows) + 1;
                foreach (var rows in oldInstances)
                {
                    if (remaining.TryGetValue(rows, out int count) && count > 0)
                    {
                        remaining[rows] = count - 1;
                        continue;
                    }
                    foreach (var row in rows)
                        changes.Add(new(oldSection.Section, row, null, oldSection.Coverage, newSection.Coverage, AmbiguousIdentity: true));
                }
                foreach (var rows in newInstances)
                {
                    if (remaining[rows] == 0) continue;
                    remaining[rows]--;
                    foreach (var row in rows)
                        changes.Add(new(oldSection.Section, null, row, oldSection.Coverage, newSection.Coverage, AmbiguousIdentity: true));
                }
            }
        }
        Changes = Array.AsReadOnly(changes.ToArray());
        Display = new(before, after, Changes, SourceChanges);
    }
    private static (CharacterRuleEffectMetric Metric, string Qualifier) Key(CharacterRuleEffectValue v)
        => (v.Metric, v.Qualifier);

    private static bool SamePayload(CharacterRuleEffectValue? before, CharacterRuleEffectValue? after)
        => before is null || after is null ? before is null && after is null
            : before.State == after.State && before.Number == after.Number && before.Text == after.Text
                && before.Flag == after.Flag && before.UnavailableReason == after.UnavailableReason;

    private sealed class InstanceComparer : IEqualityComparer<CharacterRuleEffectValue[]>
    {
        public bool Equals(CharacterRuleEffectValue[]? before, CharacterRuleEffectValue[]? after)
            => ReferenceEquals(before, after) || before is not null && after is not null
                && before.Length == after.Length && before.Zip(after).All(pair =>
                    Key(pair.First) == Key(pair.Second) && SamePayload(pair.First, pair.Second));

        public int GetHashCode(CharacterRuleEffectValue[] rows)
        {
            // Section projection has already sorted each occurrence by metric and qualifier.
            var hash = new HashCode();
            foreach (var row in rows)
            {
                hash.Add(Key(row)); hash.Add(row.State); hash.Add(row.Number);
                hash.Add(row.Text); hash.Add(row.Flag); hash.Add(row.UnavailableReason);
            }
            return hash.ToHashCode();
        }
    }
    internal CharacterCurrentRuleEnvironmentExecution BeforeIssuer { get; }
    internal CharacterCurrentRuleEnvironmentExecution AfterIssuer { get; }
    public CharacterRuleEffectProjection Before { get; }
    public CharacterRuleEffectProjection After { get; }
    public IReadOnlyList<CharacterRuleEffectChange> Changes { get; }
    public IReadOnlyList<string> SourceChanges { get; }
    public CharacterRuleEffectComparisonDisplay Display { get; }
    public bool IsComplete => Before.IsComplete && After.IsComplete;
}

public sealed partial class CharacterCurrentRuleEnvironmentExecution
{
    /// <summary>Both exact records are freshly readmitted. There is no supplied-result overload.
    /// Early Creation and unsupported sheet sections return explicit coverage, not an admission failure.</summary>
    public CharacterCurrentRuleEffectComparison? CompareRuleEffectsFrom(
        CharacterCurrentRuleEnvironmentExecution previous,
        WorkspaceStoredDocument before, CharacterCreationRuleSourceCapture beforeCapture,
        WorkspaceStoredDocument after, CharacterCreationRuleSourceCapture afterCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        cancellationToken.ThrowIfCancellationRequested();
        if (before.Id != after.Id) return null;
        try
        {
            var oldExecution = previous.Reevaluate(before, beforeCapture, queries, cancellationToken);
            var newExecution = Reevaluate(after, afterCapture, queries, cancellationToken);
            if (oldExecution is null || newExecution is null) return null;
            var oldProjection = oldExecution.CalculateRuleEffects2912(cancellationToken);
            var newProjection = newExecution.CalculateRuleEffects2912(cancellationToken);
            var comparison = new CharacterCurrentRuleEffectComparison(previous, this,
                oldProjection, newProjection, DescribeUpdateFrom(previous));
            // Final fences cover both original issuers and the exact fresh executions whose
            // outputs were projected. Validate retained archives, current bindings and owned
            // frozen reports; do not execute and discard another pair of fourteen domains.
            if (!previous.RevalidateDisplayObservation(before, beforeCapture, queries, cancellationToken)
                || !RevalidateDisplayObservation(after, afterCapture, queries, cancellationToken)
                || !oldExecution.RevalidateDisplayObservation(before, beforeCapture, queries, cancellationToken)
                || !newExecution.RevalidateDisplayObservation(after, afterCapture, queries, cancellationToken)) return null;
            return comparison;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or OverflowException or System.Xml.XmlException or System.Text.Json.JsonException)
        { return null; }
    }

    /// <summary>Optional confirmation-time observation check; existing owner/store/lease fences remain required.</summary>
    public bool RevalidateRuleEffects(CharacterCurrentRuleEffectComparison observed,
        CharacterCurrentRuleEnvironmentExecution previous,
        WorkspaceStoredDocument before, CharacterCreationRuleSourceCapture beforeCapture,
        WorkspaceStoredDocument after, CharacterCreationRuleSourceCapture afterCapture,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(observed.BeforeIssuer, previous) || !ReferenceEquals(observed.AfterIssuer, this)) return false;
        var fresh = CompareRuleEffectsFrom(previous, before, beforeCapture, after, afterCapture, queries, cancellationToken);
        return fresh is not null
            && CharacterCreationFinalizationDigest.Compute(new { fresh.Before, fresh.After, fresh.SourceChanges })
                == CharacterCreationFinalizationDigest.Compute(new { observed.Before, observed.After, observed.SourceChanges });
    }

    private CharacterRuleEffectProjection CalculateRuleEffects2912(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Exactly the resolver held by this admission, including the selected semantics and retained packs.
        var sheet = CharacterSheetDerivedQuery.ParseForRuleEffects2912(context.CharacterXml, resolution.Resolver, out var ownerValues,
            CharacterCreationPriorityGrantAuthority3563.Capture(context.Current, resolution.Resolver));
        var sections = CharacterRuleEffectValues2912.Sheet(sheet);
        if (sheet.UnavailableReason is not null && context.Stage == CharacterCurrentRuleStage.Creation)
        {
            bool NoSelection(CharacterCurrentRuleDomain domain) => executions.Any(e => e.Domain == domain
                && e.State == CharacterCurrentRuleConsumptionState.NotConfigured && e.SelectedKeys.Count == 0);
            for (int index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                bool awaitingSelection = section.Section is CharacterRuleEffectSection.Attributes
                    or CharacterRuleEffectSection.Statistics or CharacterRuleEffectSection.Movement
                    ? NoSelection(CharacterCurrentRuleDomain.Metatypes)
                    : section.Section == CharacterRuleEffectSection.Skills && NoSelection(CharacterCurrentRuleDomain.Skills);
                if (awaitingSelection && section.Values.All(v => v.State == CharacterRuleEffectValueState.Unavailable))
                    sections[index] = new(section.Section, CharacterRuleEffectCoverage.NotSelected, [],
                        ["The admitted Creation state has not selected this section.", sheet.UnavailableReason]);
            }
        }
        sections.Add(CharacterRuleEffectValues2912.Section(CharacterRuleEffectSection.Equipment, ownerValues ?? [],
            ownerValues is null ? [sheet.UnavailableReason ?? "Equipped owner calculation unavailable."] : []));
        foreach (var execution in executions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = catalogs.Single(c => c.Domain == execution.Domain);
            if (!resolution.HasExecutedCurrentRuleDomain(context, catalog, execution))
                throw new InvalidOperationException("Rule calculation no longer belongs to this admission.");
            sections.Add(WithCareerResources2974(
                CharacterCurrentRuleEnvironmentConsumers.ReadRuleEffects2912(context, catalog, execution), cancellationToken));
        }
        if (!context.VerifyRetainedArchive()) throw new InvalidOperationException("Retained source changed.");
        var current = context.Current;
        return new(new(current.Id.ToString(), current.ContentRevision, current.SavedRevision, context.BindingDigest,
            CharacterCreationFinalizationDigest.ComputeUtf8(context.CharacterXml), current.Document.AuxiliaryStateDigest,
            CaptureDigest, RequiredPackageSetDigest, ExecutionProfile, ExecutionProfileFingerprint, outcomeDigest), sections);
    }
}
