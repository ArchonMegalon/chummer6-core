using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    internal sealed partial class QualityKarmaLimitExecution947
    {
        // Captured only by the existing full/selected canonical evaluation factories.
        // A source observation, public result or copied invocation cannot create this capability.
        private readonly QualityCostSource973? qualityCost973;
        private sealed record QualityCostSource973(string LeafXmlDigest, CharacterCreationQualityCostPolicy Policy);

        private static QualityCostSource973? CaptureCost973(CharacterCreationEffectivePolicyContext policy,
            XElement profile, CharacterCreationQualityCostPolicy? evaluatedPolicy)
        {
            // Null is the canonical evaluator's historical policy representation. An
            // explicit source leaf is still required, even for that policy's multiplier.
            var cost = evaluatedPolicy ?? CharacterCreationQualityCostPolicy.Default;
            if (policy.Consumers?.Qualities.CostPolicy != cost
                || profile.Elements("karmacost").ToArray() is not [var costs] || costs.HasAttributes
                || costs.Elements("karmaquality").ToArray() is not [var leaf]
                || leaf.HasAttributes || leaf.HasElements || leaf.Nodes().Any(node => node.GetType() != typeof(XText))
                || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int multiplier)
                || multiplier < 0 || multiplier != cost.KarmaMultiplier) return null;
            return new(XmlDigest947(leaf), cost with { });
        }

        internal static bool TryReadCost973(CharacterCreationRuleSourceScopeInvocation invocation,
            string profileId, string profileInputsDigest, string profileXmlDigest, string leafXmlDigest,
            out CharacterCreationQualitiesCompletionEvaluation? evaluation,
            out CharacterCreationQualityCostPolicy? costPolicy, out bool noTarget, out string? executionDigest)
        {
            evaluation = null; costPolicy = null; noTarget = false; executionDigest = null;
            if (!qualityInvocations947.TryGetValue(invocation, out var bound)
                || bound.Completed.qualityCost973 is not { } cost || leafXmlDigest != cost.LeafXmlDigest
                || !TryRead947(invocation, profileId, profileInputsDigest, profileXmlDigest,
                    bound.Completed.leafXmlDigest, out var actual, out bool empty, out var limitExecution)) return false;
            // TryRead947 verifies the exact completed consumer, request, authority,
            // capture, profile and candidate bytes and detaches the canonical result.
            // EvaluateSelectionCore already called QualityCostRules.TryCalculate for
            // these selections AND grants, including an actually evaluated empty set.
            evaluation = actual;
            costPolicy = cost.Policy with { };
            noTarget = empty;
            executionDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-quality-karma-cost", LimitExecution = limitExecution,
                cost.LeafXmlDigest, CostPolicy = cost.Policy, Result = bound.Completed.result, NoTarget = empty
            });
            return true;
        }
    }
}
