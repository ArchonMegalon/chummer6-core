using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Scalars and hashes only: neither a mutable result nor a public receipt can
    // create this capability. Full and selected routes execute it independently.
    private sealed record BoundCarryover995(string PolicyContentDigest, string ProfileXmlDigest,
        string LeafXmlDigest, string InvocationDigest, int Remaining, int Carried, string ExecutionDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundCarryover995>
        carryoverInvocations995 = new();

    private void BindProfileCarryover995(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationResourcesService.CompleteFundingExecution? funding,
        CharacterCreationCompleteContactsExecution? contacts)
    {
        var policy = Inputs.Contacts.CarryoverPolicy;
        var bootstrap = Inputs.Contacts.BootstrapBinding;
        if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            || !CanAttemptSourceScopes696(request) || funding is null || contacts is null
            || !original.MatchesInputs(Inputs.SnapshotDigest)
            || Inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(Inputs)
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || request.InputsSnapshotDigest != Inputs.SnapshotDigest
            || authority.RequestDigest != requestDigest || requestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
            || authority.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy?.PolicyDigest
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != authority.RequestDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != Inputs.OriginalCharacter?.WorkspaceId
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills, invocation.Skills718)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills.Attributes, invocation.Attributes718)
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(policy)
            || policy.SettingsProfileId != bootstrap.SettingsProfileId
            || policy.RawProfileInputsDigest != bootstrap.RawProfileInputsDigest
            || invocation.CreationDraft718?.SettingsProfileId != policy.SettingsProfileId
            || !funding.TryGetCarryoverBudget995(authority, out int resources, out decimal nuyen, out decimal nuyenLimit)
            || policy.MaximumNuyen != nuyenLimit || nuyen < 0
            || contacts.KarmaCostFor995(authority) is not int contactCost) return;

        var effective = Inputs.EffectivePolicy!;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (effective.SettingsProfileId != policy.SettingsProfileId || effective.ProfileInputsDigest != policy.RawProfileInputsDigest
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(effective, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(effective, source)) return;
        var profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        var containers = profile.Elements().Where(node => node.Name.LocalName == "karmacost").ToArray();
        if (profile.HasAttributes || containers is not [var costs] || costs.Name != "karmacost" || costs.HasAttributes
            || costs.Elements().Where(node => node.Name.LocalName == "karmacarryover").ToArray() is not [var leaf]
            || leaf.Name != "karmacarryover" || leaf.HasAttributes
            || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap)
            || cap < 0 || cap != policy.MaximumKarma) return;

        // The same canonical balance and cap calculations drive actual Finish.
        // This runs before source coverage/Complete admission; it grants neither.
        int remaining = CharacterCreationKarmaFinalizationBudgetRules.RemainingCreationKarma995(
            resources, skills.Skills.Sum(row => row.KarmaCost), contactCost);
        if (remaining < 0 || request.Selections.KarmaToReserve < 0 || remaining < request.Selections.KarmaToReserve) return;
        int carried = CharacterCreationKarmaFinalizationBudgetRules.CarryCreationKarma995(remaining, policy);
        string profileXml = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        string leafXml = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
        string binding = CarryoverInvocationDigest995(invocation);
        string execution = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-creation-karma-carryover", Authority = CharacterCreationFinalizationDigest.Compute(authority),
            Inputs.SnapshotDigest, Policy = policy, ProfileXml = profileXml, LeafXml = leafXml, Invocation = binding,
            Funding = funding.EvaluationDigest, Contacts = contacts.DigestFor(authority),
            Resources = resources, Skills = skills.Skills.Sum(row => row.KarmaCost), ContactsKarma = contactCost,
            Remaining = remaining, Carried = carried
        });
        carryoverInvocations995.Add(invocation, new(CharacterCreationFinalizationDigest.Compute(policy),
            profileXml, leafXml, binding, remaining, carried, execution));
    }

    internal static bool TryReadCarryover995(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationKarmaCarryoverPolicy policy, string profileXmlDigest, string leafXmlDigest,
        out int remaining, out int carried, out string? executionDigest)
    {
        remaining = 0; carried = 0; executionDigest = null;
        if (!carryoverInvocations995.TryGetValue(invocation, out var bound)
            || bound.PolicyContentDigest != CharacterCreationFinalizationDigest.Compute(policy)
            || bound.ProfileXmlDigest != profileXmlDigest || bound.LeafXmlDigest != leafXmlDigest
            || bound.InvocationDigest != CarryoverInvocationDigest995(invocation)) return false;
        remaining = bound.Remaining; carried = bound.Carried; executionDigest = bound.ExecutionDigest;
        return true;
    }

    private static string CarryoverInvocationDigest995(CharacterCreationRuleSourceScopeInvocation invocation)
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
}
