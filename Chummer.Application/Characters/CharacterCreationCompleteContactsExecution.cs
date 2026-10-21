using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Private, transient evidence of canonical contact pricing. A projection or a
/// matching digest cannot manufacture this execution. Source/history admission
/// remains with the full reader and independent selected replay.
/// </summary>
internal sealed class CharacterCreationCompleteContactsExecution
{
    private readonly string authorityDigest;
    private readonly string evaluationDigest;
    private readonly CharacterCreationKarmaContactsPolicy contactsPolicy;
    private readonly string attributesDigest;
    private readonly CharacterCreationContactBudget contactBudget;
    private readonly int additionalKarma995;

    internal int? KarmaCostFor995(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is null ? null : additionalKarma995;

    private CharacterCreationCompleteContactsExecution(CharacterCreationCompleteAssessment authority,
        SelectedCompletionContactsPrice price, CharacterCreationKarmaContactsPolicy policy,
        CharacterCreationAttributesPreview attributes)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        contactsPolicy = Detach(policy);
        attributesDigest = CharacterCreationFinalizationDigest.Compute(attributes);
        contactBudget = Detach(price.ContactBudget);
        additionalKarma995 = price.AdditionalKarmaCost!.Value;
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest,
            Operations = new[] { CharacterCreationConsumerPolicyOperation.ContactPointAllowance,
                CharacterCreationConsumerPolicyOperation.ContactGroupKarma },
            Result = price
        });
    }

    internal string? DigestFor(CharacterCreationCompleteAssessment? authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                ? evaluationDigest : null;

    internal static CharacterCreationCompleteContactsExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            || inputs is null || review is null) return null;
        inputs = Detach(inputs);
        review = Detach(review);
        authority = Detach(authority);
        if (inputs.Contacts is null || review.Contacts is null || review.Skills is null
            || review.UpstreamBudget is null || review.Blockers.Count != 0
            || !CanEvaluate(authority, inputs.SnapshotDigest, review.Request)
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy!, inputs)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, review.Request, ruleSourceExecution)))
            return null;

        // This also validates the full contact input binding and independently
        // projects the proposed attributes and quality effects before pricing.
        var executed = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
            review.Request.Selections.AttributeAllocations, review.Request.Selections.QualityOptionIds,
            review.Request.Selections.Contacts, cancellationToken);
        if (executed.Outcome != CharacterCreationContactOutcomes.Available || executed.Blockers.Count != 0
            || executed.Value is not { AdditionalKarmaCost: >= 0 } price || price.Blockers.Count != 0
            || !Same(price, review.Contacts)
            || !Same(price.Upstream.Attributes, review.Skills.Attributes)
            || !Same(price.Upstream.Qualities, review.UpstreamBudget.Qualities.Qualities)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(authority, new(price.Contacts, price.ContactBudget, price.HighPlacesBudget,
            price.AdditionalKarmaCost, price.Blockers) { GroupKarma = price.GroupKarma },
            inputs.Contacts.Policy, price.Upstream.Attributes);
    }

    internal static CharacterCreationCompleteContactsExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompletionOriginalSelections original,
        IReadOnlyList<CharacterContactRuleEffect> effects, CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            || witness is null || request is null || result is null || original is null || effects is null) return null;
        witness = Detach(witness);
        request = Detach(request);
        result = Detach(result);
        original = Detach(original);
        effects = Detach(effects);
        authority = Detach(authority);
        if (witness.ContactsPolicy is null || witness.Skills?.Attributes is null || witness.Qualities is null
            || result.Contacts is null || result.Skills is null || original.Contacts is null || effects is null
            || !CanEvaluate(authority, witness.ProvenanceInputsSnapshotDigest, request)
            || witness.ProposalDigest != CharacterCreationFinalizationDigest.Compute(request)
            || witness.WitnessDigest != CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness)
            || result.WitnessDigest != witness.WitnessDigest
            || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, original, effects)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, effects, ruleSourceExecution)))
            return null;

        // Do not take effective CHA or quality effects from a supplied result.
        // These canonical projections use this route's frozen authority and the
        // exact request, independently of the full route's contact execution.
        var session = CharacterCreationAttributesService.CreateDraftSession(witness.Skills.Attributes);
        var attributes = session?.Preview(request.Selections.AttributeAllocations).Value;
        if (attributes is null
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate)
            || !Same(attributes, result.Skills.Attributes)) return null;
        decimal karma = attributes.CreationKarmaBudget.Used;
        if (karma < 0 || karma > int.MaxValue || karma != decimal.Truncate(karma)
            || !CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness.Qualities,
                request.Selections.QualityOptionIds, witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                (int)karma, out var qualities)
            || qualities is null || qualities.Blockers.Count != 0 || !Same(qualities, result.Qualities)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var executed = CharacterCreationContactsService.PriceSelectedCompletion(new(witness.ContactsPolicy,
            witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], effects,
            original.Contacts, attributes.Attributes, qualities.Selections, request.Selections.Contacts,
            witness.ContactsSourceProvenanceSeed, [],
            witness.Qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
            witness.Qualities.QualityKarmaLimit, witness.Qualities.MayExceedPositiveQualityLimit), cancellationToken);
        if (executed.Outcome != CharacterCreationContactOutcomes.Available || executed.Blockers.Count != 0
            || executed.Value is not { AdditionalKarmaCost: >= 0 } price || price.Blockers.Count != 0
            || !Same(price, result.Contacts)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(authority, price, witness.ContactsPolicy, attributes);
    }

    // Reuse the successful canonical consumer above. This is a source-accounting
    // capability, not another expression interpreter or a serializable receipt.
    // Bind the exact admitted expression, including supported house rules. The
    // canonical consumer already evaluated its grammar, attributes and budget;
    // comparing a standard formula string is not execution evidence.
    internal void BindSourceInvocation962(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request)
    {
        if (DigestFor(authority) is null
            || !CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
            || !CanEvaluate(authority, inputs.SnapshotDigest, request)
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || authority.PolicyDigest != inputs.EffectivePolicy?.PolicyDigest
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != authority.RequestDigest
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest
            || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
            || !Same(contactsPolicy, inputs.Contacts.Policy)
            || attributesDigest != CharacterCreationFinalizationDigest.Compute(invocation.Attributes718)
            || !Same(invocation.Attributes718, invocation.Skills718?.Attributes)
            || invocation.CreationDraft718?.SettingsProfileId != contactsPolicy.SettingsProfileId
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        var policy = inputs.EffectivePolicy!;
        var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (policy.SettingsProfileId != contactsPolicy.SettingsProfileId
            || policy.ProfileInputsDigest != contactsPolicy.RawProfileInputsDigest
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return;
        var profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        var leaves = profile.Elements().Where(node => node.Name.LocalName == "contactpointsexpression").ToArray();
        if (profile.HasAttributes || leaves is not [var leaf] || leaf.Name != "contactpointsexpression"
            || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || leaf.Value != contactsPolicy.ContactPointsExpression) return;
        // An explicit expression bypasses the resolver's legacy total/multiplier
        // shim. Those siblings receive no coverage. The entire profile digest
        // still binds them, as well as group/quality pricing dependencies.
        sourceInvocations962.Add(invocation, new(this, InvocationDigest962(invocation),
            XmlDigest962(profile), XmlDigest962(leaf)));
    }

    internal static bool TryReadSource962(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationKarmaContactsPolicy policy, string profileXmlDigest, string leafXmlDigest,
        out CharacterCreationContactBudget? budget, out string? executionDigest)
    {
        budget = null; executionDigest = null;
        if (!sourceInvocations962.TryGetValue(invocation, out var bound)
            || !Same(bound.Completed.contactsPolicy, policy)
            || bound.ProfileXmlDigest != profileXmlDigest || bound.LeafXmlDigest != leafXmlDigest
            || bound.InvocationDigest != InvocationDigest962(invocation)) return false;
        budget = Detach(bound.Completed.contactBudget);
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-contact-point-expression", bound.Completed.authorityDigest,
            bound.Completed.evaluationDigest, bound.Completed.contactsPolicy, bound.Completed.attributesDigest,
            bound.InvocationDigest, bound.ProfileXmlDigest, bound.LeafXmlDigest
        });
        return true;
    }

    private sealed record BoundSource962(CharacterCreationCompleteContactsExecution Completed,
        string InvocationDigest, string ProfileXmlDigest, string LeafXmlDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundSource962>
        sourceInvocations962 = new();

    private static string InvocationDigest962(CharacterCreationRuleSourceScopeInvocation invocation)
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

    private static string XmlDigest962(XElement node)
        => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));

    private static bool CanEvaluate(CharacterCreationCompleteAssessment authority, string snapshot,
        CharacterCreationCompletionReviewRequest request)
        => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.Phase == CharacterCreationCompletePhase.Authority
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.Selections?.Contacts is not null
            && authority.InputsSnapshotDigest == snapshot && snapshot == request.InputsSnapshotDigest
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections)
            && CharacterCreationFinalizationDigest.IsCanonical(snapshot);

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static T Detach<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
