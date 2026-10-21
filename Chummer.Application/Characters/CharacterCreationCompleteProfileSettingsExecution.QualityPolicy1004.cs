using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    internal sealed partial class QualityKarmaLimitExecution947
    {
        private readonly IReadOnlyDictionary<string, QualityPolicySource1004> qualityPolicy1004;
        private sealed record QualityPolicySource1004(string LeafXmlDigest, bool Value,
            CharacterCreationQualityCostPolicy CostPolicy);

        // Only the successful full/selected evaluator factories reach this capture.
        // In particular, resolved booleans and a request with no selections are not execution.
        private static IReadOnlyDictionary<string, QualityPolicySource1004> CaptureQualityPolicy1004(
            CharacterCreationEffectivePolicyContext policy, XElement profile,
            CharacterCreationQualitiesCompletionEvaluation result, CharacterCreationQualityCostPolicy? evaluatedPolicy)
        {
            var captured = new Dictionary<string, QualityPolicySource1004>(StringComparer.Ordinal);
            var cost = evaluatedPolicy ?? CharacterCreationQualityCostPolicy.Default;
            if (policy.Consumers?.Qualities is not { } consumer || consumer.CostPolicy != cost
                || consumer.QualityKarmaLimit != result.PositiveQualityBudget.Total
                || consumer.QualityKarmaLimit != result.NegativeQualityBudget.Total
                || consumer.MayExceedPositiveQualityLimit != result.PositiveQualityBudget.MayExceed
                || consumer.MayExceedNegativeQualityLimit != result.NegativeQualityBudget.MayExceed) return captured;
            foreach (var (field, expected) in new (string, bool)[]
            {
                ("exceedpositivequalities", result.PositiveQualityBudget.MayExceed),
                ("exceedpositivequalitiescostdoubled", cost.DoublePositiveExcess),
                ("exceednegativequalities", result.NegativeQualityBudget.MayExceed),
                ("exceednegativequalitiesnobonus", cost.CapNegativeRebate)
            })
            {
                // Count by local name before requiring the unqualified scalar, so a
                // namespaced duplicate cannot borrow execution from the ordinary leaf.
                var leaves = profile.Elements().Where(node => node.Name.LocalName == field).Take(2).ToArray();
                if (leaves is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || !bool.TryParse(text.Value, out bool value) || value != expected) continue;
                captured.Add(field, new(XmlDigest947(leaf), value, cost with { }));
            }
            return captured;
        }

        internal static bool TryReadQualityPolicy1004(CharacterCreationRuleSourceScopeInvocation invocation,
            string profileId, string profileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
            out CharacterCreationQualitiesCompletionEvaluation? evaluation,
            out CharacterCreationQualityCostPolicy? costPolicy, out bool value, out bool noTarget, out string? executionDigest)
        {
            evaluation = null; costPolicy = null; value = false; noTarget = false; executionDigest = null;
            if (field is null || !qualityInvocations947.TryGetValue(invocation, out var bound)
                || !bound.Completed.qualityPolicy1004.TryGetValue(field, out var source)
                || leafXmlDigest != source.LeafXmlDigest
                || !TryRead947(invocation, profileId, profileInputsDigest, profileXmlDigest,
                    bound.Completed.leafXmlDigest, out var actual, out bool empty, out var limitExecution)) return false;
            // Reuse the exact candidate/source/authority/workspace binding. The returned
            // result includes both cost budgets and remaining Karma, and is detached.
            evaluation = actual;
            costPolicy = source.CostPolicy with { };
            value = source.Value;
            noTarget = empty;
            executionDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-quality-limit-policy", LimitExecution = limitExecution,
                Field = field, source.LeafXmlDigest, Value = value, CostPolicy = source.CostPolicy,
                Result = bound.Completed.result, NoTarget = empty
            });
            return true;
        }
    }
}
