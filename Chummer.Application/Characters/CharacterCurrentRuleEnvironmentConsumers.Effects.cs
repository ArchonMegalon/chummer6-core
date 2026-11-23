using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Sidecar output of the same kernel invocation. It does not enter historical outcome hashes,
    // cannot be supplied by callers, and cannot outlive its exact report identity.
    private sealed record DerivedOutput2912(IReadOnlyList<CharacterRuleEffectValue> Values,
        IReadOnlyList<string> Reasons, bool ExceedsDisplayBound = false);
    private sealed record IssuedOutput2912(CharacterCurrentRuleEnvironmentContext Current,
        CharacterCurrentRuleCatalogValidation Catalog, string ReportDigest, DerivedOutput2912 Output);
    private static readonly ConditionalWeakTable<object, DerivedOutput2912> DerivedOutcomes2912 = new();
    private static readonly ConditionalWeakTable<CharacterCurrentRuleDomainExecution, IssuedOutput2912> DerivedReports2912 = new();

    private static T TagRuleEffects2912<T>(T outcome, IEnumerable<CharacterRuleEffectValue> values,
        params string[] reasons) where T : class
    {
        // Scalars are immutable; detach the collection before any result escapes the consumer.
        var rows = Array.AsReadOnly(values.Take(8193).ToArray());
        // A display limit must not change ordinary domain admission. Only the effect-review
        // producer refuses an oversized observation; no prefix is presented as a calculation.
        DerivedOutcomes2912.Add(outcome, rows.Count > 8192
            ? new([], [], ExceedsDisplayBound: true)
            : new(rows, Array.AsReadOnly(reasons.ToArray())));
        return outcome;
    }

    private static T ForwardRuleEffects2912<T>(T outer, object? inner) where T : class
    {
        if (inner is not null && DerivedOutcomes2912.TryGetValue(inner, out var output))
            DerivedOutcomes2912.Add(outer, output);
        return outer;
    }

    private static CharacterCurrentRuleDomainExecution IssueRuleEffects2912(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        CharacterCurrentRuleDomainExecution report, object? outcome)
    {
        if (report.IsComplete && outcome is not null && DerivedOutcomes2912.TryGetValue(outcome, out var output))
            DerivedReports2912.Add(report, new(current, catalog, report.ContentDigest, output));
        return report;
    }

    internal static CharacterRuleEffectSectionProjection ReadRuleEffects2912(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        CharacterCurrentRuleDomainExecution report)
    {
        var section = report.Domain == CharacterCurrentRuleDomain.Skills ? CharacterRuleEffectSection.SkillCosts
            : Enum.Parse<CharacterRuleEffectSection>(report.Domain.ToString());
        if (!report.IsComplete || report.ContextDigest != current.BindingDigest
            || report.CatalogDigest != catalog.ContentDigest)
            throw new InvalidOperationException("Foreign or changed domain calculation.");
        if (report.State == CharacterCurrentRuleConsumptionState.NotConfigured && report.SelectedKeys.Count == 0)
            return new(section, CharacterRuleEffectCoverage.NotSelected, [],
                ["The canonical consumer reports no current selection; no numeric result is inferred from an empty basket."]);
        var values = new List<CharacterRuleEffectValue>();
        foreach (string key in report.SelectedKeys.Distinct(StringComparer.Ordinal))
        {
            var selected = catalog.Entries.Single(e => e.Key == key && e.State == CharacterCurrentRuleEntryState.AvailableNotSelected);
            values.Add(CharacterRuleEffectValues2912.Text(key, selected.Name, CharacterRuleEffectMetric.Selected,
                selected.Key, "catalog-selection"));
        }
        if (!DerivedReports2912.TryGetValue(report, out var issued))
        {
            return new(section, report.State == CharacterCurrentRuleConsumptionState.NotConfigured && values.Count == 0
                    ? CharacterRuleEffectCoverage.NotSelected : CharacterRuleEffectCoverage.Partial,
                values, [report.State == CharacterCurrentRuleConsumptionState.NotConfigured
                    ? "No current selection; later creation calculations are not available."
                    : "The canonical consumer admitted this domain but has not exposed all calculated display values."]);
        }
        if (!ReferenceEquals(issued.Current, current) || !ReferenceEquals(issued.Catalog, catalog)
            || issued.ReportDigest != report.ContentDigest)
            throw new InvalidOperationException("Derived output does not belong to this report.");
        if (issued.Output.ExceedsDisplayBound)
            throw new InvalidOperationException("Rule calculation exceeds display bound.");
        values.AddRange(issued.Output.Values);
        return CharacterRuleEffectValues2912.Section(section, values, issued.Output.Reasons);
    }
}
