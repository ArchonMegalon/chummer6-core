using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    // The constructor and registration are private. Each factory actually runs
    // the canonical evaluator; a public budget/result can only be compared with it.
    internal sealed partial class QualityKarmaLimitExecution947
    {
        private readonly string authorityDigest, inputsDigest, policyContentDigest, requestDigest;
        private readonly string profileId, profileInputsDigest, profileXmlDigest, leafXmlDigest;
        private readonly string qualityAuthorityDigest, skillsDigest;
        private readonly CharacterCreationQualitiesCompletionEvaluation result, candidateResult;

        private QualityKarmaLimitExecution947(CharacterCreationCompleteAssessment authority,
            CharacterCreationEffectivePolicyContext policy, XElement profile, XElement leaf,
            string qualityAuthorityDigest, CharacterCreationAttributeSkillsPreview skills,
            CharacterCreationQualitiesCompletionEvaluation result, CharacterCreationQualityCostPolicy? costPolicy,
            CharacterCreationQualitiesCompletionEvaluation? candidateResult = null)
        {
            qualityCost973 = CaptureCost973(policy, profile, costPolicy);
            qualityPolicy1004 = CaptureQualityPolicy1004(policy, profile, result, costPolicy);
            authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
            inputsDigest = authority.InputsSnapshotDigest;
            policyContentDigest = CharacterCreationFinalizationDigest.Compute(policy);
            requestDigest = authority.RequestDigest;
            profileId = policy.SettingsProfileId;
            profileInputsDigest = policy.ProfileInputsDigest;
            profileXmlDigest = XmlDigest947(profile);
            leafXmlDigest = XmlDigest947(leaf);
            this.qualityAuthorityDigest = qualityAuthorityDigest;
            skillsDigest = CharacterCreationFinalizationDigest.Compute(skills);
            this.result = Copy947(result);
            this.candidateResult = Copy947(candidateResult ?? result);
        }

        internal static QualityKarmaLimitExecution947? ExecuteFull(
            CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
            CharacterCreationCompleteAssessment authority,
            out CharacterCreationFoundationResult<CharacterCreationAttributeQualitiesPreview> evaluated,
            CancellationToken cancellationToken)
        {
            evaluated = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Lifestyles.Qualities,
                review.Request.Selections.AttributeAllocations, review.Request.Selections.QualityOptionIds, cancellationToken);
            if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Blockers.Count != 0
                || evaluated.Value is not { } actual || review.UpstreamBudget is null || review.Skills is null
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(actual, review.UpstreamBudget.Qualities))
                return null;
            var quality = inputs.Lifestyles.Qualities.Authority;
            return Create947(inputs.EffectivePolicy!, inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                review.Request, authority, quality.SettingsProfileId, quality.ProfileDigest, quality.QualityKarmaLimit,
                quality.MayExceedPositiveQualityLimit, quality.MayExceedNegativeQualityLimit,
                quality.AuthorityDigest, quality.AuthorityDigest, review.Skills, actual.Qualities, quality.CostPolicy);
        }

        internal static QualityKarmaLimitExecution947? ExecuteSelected(
            CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
            CharacterCreationSelectedCompletionEvaluation actual, CharacterCreationCompleteAssessment authority,
            out CharacterCreationQualitiesCompletionEvaluation? evaluated, CancellationToken cancellationToken,
            CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluated = null;
            decimal cost = actual.Skills.Attributes.CreationKarmaBudget.Used;
            if (cost < 0 || cost > int.MaxValue || cost != decimal.Truncate(cost)
                || !CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness.Qualities,
                    request.Selections.QualityOptionIds, witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                    (int)cost, out evaluated) || evaluated is null || evaluated.Blockers.Count != 0
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(evaluated, actual.Qualities))
                return null;
            if (witness.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
                || witness.PolicyRequirement != request.PolicyRequirement
                || witness.ProvenanceInputsSnapshotDigest != request.InputsSnapshotDigest) return null;
            // Source receipts use an independent execution of the resolved full inputs.
            // The selected witness carries post-review consumption and a different
            // authority envelope; neither can replace the source execution identity.
            if (ruleSourceExecution?.Inputs is not { } inputs
                || inputs.SnapshotDigest != witness.ProvenanceInputsSnapshotDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.EffectivePolicy,
                    witness.EffectivePolicy! with { SelectedConsumption = null })
                || !CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(inputs.Lifestyles.Qualities.Authority,
                    inputs.Lifestyles.Qualities.SelectedOptionIds, request.Selections.QualityOptionIds, out var selected)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selected, witness.Qualities))
                return null;
            var canonical = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Lifestyles.Qualities,
                request.Selections.AttributeAllocations, request.Selections.QualityOptionIds, cancellationToken);
            if (canonical.Outcome != CharacterCreationFoundationOutcomes.Success || canonical.Blockers.Count != 0
                || canonical.Value is not { } completed
                || inputs.Lifestyles.Qualities.Binding.CreationKarmaTotal != witness.Skills.PrerequisiteDraft.CreationKarmaTotal
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(completed.Attributes, actual.Skills.Attributes))
                return null;
            var quality = inputs.Lifestyles.Qualities.Authority;
            return Create947(inputs.EffectivePolicy!, inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                request, authority, quality.SettingsProfileId, quality.ProfileDigest, quality.QualityKarmaLimit,
                quality.MayExceedPositiveQualityLimit, quality.MayExceedNegativeQualityLimit,
                quality.AuthorityDigest, quality.AuthorityDigest, actual.Skills, completed.Qualities, quality.CostPolicy,
                evaluated);
        }

        private static QualityKarmaLimitExecution947? Create947(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationCompletionReviewRequest request,
            CharacterCreationCompleteAssessment authority, string profileId, string profileInputsDigest, int limit,
            bool positiveMayExceed, bool negativeMayExceed, string qualityAuthorityDigest, string resultAuthorityDigest,
            CharacterCreationAttributeSkillsPreview skills, CharacterCreationQualitiesCompletionEvaluation result,
            CharacterCreationQualityCostPolicy? costPolicy,
            CharacterCreationQualitiesCompletionEvaluation? candidateResult = null)
        {
            if (!CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
                || authority.Phase != CharacterCreationCompletePhase.Authority
                || authority.InputsSnapshotDigest != request.InputsSnapshotDigest
                || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
                || authority.PolicyDigest != policy.PolicyDigest || policy.SettingsProfileId != profileId
                || policy.ProfileInputsDigest != profileInputsDigest
                || !TrySource(policy, source, out var profile)
                || profile!.Elements("qualitykarmalimit").ToArray() is not [var leaf]
                || leaf.HasAttributes || leaf.HasElements || leaf.Nodes().Any(node => node.GetType() != typeof(XText))
                || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed < 0 || parsed != limit || result.AuthorityDigest != resultAuthorityDigest
                || result.Blockers.Count != 0 || result.PositiveQualityBudget.Blockers.Count != 0
                || result.NegativeQualityBudget.Blockers.Count != 0
                || result.PositiveQualityBudget.Total != limit || result.NegativeQualityBudget.Total != limit
                || result.PositiveQualityBudget.MayExceed != positiveMayExceed
                || result.NegativeQualityBudget.MayExceed != negativeMayExceed) return null;
            return new(authority, policy, profile, leaf, qualityAuthorityDigest, skills, result, costPolicy, candidateResult);
        }

        // Complete calls this only after its original-input and compiled-graph
        // checks. A new/copied invocation has no entry in this private weak table.
        internal void Bind947(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
            CharacterCreationCompletionReviewRequest request, CharacterCreationQualitiesCompletionEvaluation? qualities)
        {
            if (authority.Phase != CharacterCreationCompletePhase.Authority
                || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
                || !CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                || inputs.SnapshotDigest != inputsDigest || request.InputsSnapshotDigest != inputsDigest
                || CharacterCreationFinalizationDigest.Compute(request) != requestDigest
                || invocation.RequestDigest != requestDigest || invocation.PolicyDigest != authority.PolicyDigest
                || inputs.EffectivePolicy is not { } policy
                || CharacterCreationFinalizationDigest.Compute(policy) != policyContentDigest
                || inputs.Lifestyles.Qualities.Authority.AuthorityDigest != qualityAuthorityDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(candidateResult, qualities)
                || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest
                || invocation.CaptureDigest != request.RuleSourceCaptureDigest
                || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
                || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
                || invocation.CreationDraft718?.SettingsProfileId != profileId
                || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
                || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != skillsDigest
                || !invocation.TryGetCompletedConsumer("profile", out _)) return;
            qualityInvocations947.Add(invocation, new(this, InvocationBinding947(invocation)));
        }

        internal static bool TryRead947(CharacterCreationRuleSourceScopeInvocation invocation,
            string profileId, string profileInputsDigest, string profileXmlDigest, string leafXmlDigest,
            out CharacterCreationQualitiesCompletionEvaluation? evaluation, out bool noTarget, out string? executionDigest)
        {
            evaluation = null; noTarget = false; executionDigest = null;
            if (!qualityInvocations947.TryGetValue(invocation, out var bound)) return false;
            var completed = bound.Completed;
            if (profileId != completed.profileId || profileInputsDigest != completed.profileInputsDigest
                || profileXmlDigest != completed.profileXmlDigest || leafXmlDigest != completed.leafXmlDigest
                || bound.InvocationDigest != InvocationBinding947(invocation)) return false;
            evaluation = Copy947(completed.result);
            // This inventory was enumerated by EvaluateSelectionCore, including
            // grants. Zero spend or absent request input cannot establish emptiness.
            noTarget = evaluation.Selections.Count == 0 && evaluation.GrantedQualities.Count == 0;
            executionDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-quality-karma-limit", completed.authorityDigest, completed.inputsDigest,
                completed.requestDigest, completed.policyContentDigest, completed.profileId,
                completed.profileInputsDigest, completed.profileXmlDigest, completed.leafXmlDigest,
                completed.qualityAuthorityDigest, completed.skillsDigest, bound.InvocationDigest,
                Result = completed.result, NoTarget = noTarget
            });
            return true;
        }

        private sealed record Bound947(QualityKarmaLimitExecution947 Completed, string InvocationDigest);
        private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, Bound947>
            qualityInvocations947 = new();

        private static string InvocationBinding947(CharacterCreationRuleSourceScopeInvocation invocation)
        {
            invocation.TryGetCompletedConsumer("profile", out var consumer);
            return CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = consumer, invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
                invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
                invocation.ResourcesExecution718, invocation.LifestyleAuthority718, invocation.Lifestyles718,
                invocation.StartingLifestyleId718, invocation.SourceExecution832, invocation.CompiledMovement832,
                invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
            });
        }

        private static string XmlDigest947(XElement node)
            => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));

        private static CharacterCreationQualitiesCompletionEvaluation Copy947(CharacterCreationQualitiesCompletionEvaluation value)
            => value with
            {
                Selections = Array.AsReadOnly(value.Selections.Select(row => row with
                    { SourceAnchorIds = Array.AsReadOnly(row.SourceAnchorIds.ToArray()) }).ToArray()),
                GrantedQualities = Array.AsReadOnly(value.GrantedQualities.Select(row => row with
                    { SourceAnchorIds = Array.AsReadOnly(row.SourceAnchorIds.ToArray()) }).ToArray()),
                PositiveQualityBudget = value.PositiveQualityBudget with
                    { Blockers = Array.AsReadOnly(value.PositiveQualityBudget.Blockers.ToArray()) },
                NegativeQualityBudget = value.NegativeQualityBudget with
                    { Blockers = Array.AsReadOnly(value.NegativeQualityBudget.Blockers.ToArray()) },
                SourceAnchorIds = Array.AsReadOnly(value.SourceAnchorIds.ToArray()),
                Blockers = Array.AsReadOnly(value.Blockers.ToArray())
            };
    }
}
