using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record ProfileInvocation3212(CharacterCreationCompleteRuleSourceExecution Owner,
        CharacterCreationCompleteAssessment Authority, string AuthorityDigest, string InvocationDigest,
        string ProfileXmlDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, ProfileInvocation3212>
        profileInvocations3212 = new();
    private CharacterCreationRuleSourceScopeInvocation? profileInvocation3212;

    // This is custody only, not a source/setting receipt. It is issued by the
    // original-aware canonical producer; source replay cannot register a copy.
    private void ResetProfileInvocation3212()
    {
        if (profileInvocation3212 is { } previous) profileInvocations3212.Remove(previous);
        profileInvocation3212 = null;
    }

    private bool MatchesProfileOwner3212(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority)
        => MatchesKarmaAuthority1208(authority) && Inputs.OriginalCharacter is { } owner
            && invocation.WorkspaceId718 is { } workspace && workspace == owner.WorkspaceId
            && Inputs.Skills.Binding.WorkspaceId == owner.WorkspaceId
            && Inputs.Skills.Binding.ContentRevision == owner.ContentRevision
            && Inputs.Skills.Binding.SavedRevision == owner.SavedRevision
            && invocation.PolicyDigest == authority.PolicyDigest && invocation.RequestDigest == requestDigest
            && invocation.CaptureDigest == Inputs.RuleSourceCaptureDigest
            && invocation.CreationDraft718?.SettingsProfileId == Inputs.EffectivePolicy!.SettingsProfileId
            && invocation.TryGetCompletedConsumer("profile", out _);

    private void BindProfileInvocation3212(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph)
    {
        ResetProfileInvocation3212();
        if (!MatchesProfileOwner3212(invocation, authority)
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(Inputs.EffectivePolicy!,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, CharacterCreationPolicyDomain.Profile)) return;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        profileInvocations3212.Add(invocation, new(this, authority, CharacterCreationFinalizationDigest.Compute(authority),
            ProfileSourceInvocationDigest3212(invocation),
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting))));
        profileInvocation3212 = invocation;
    }

    private static string ProfileSourceInvocationDigest3212(CharacterCreationRuleSourceScopeInvocation invocation)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
            invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
            invocation.ResourcesExecution718, invocation.LifestyleAuthority718, invocation.Lifestyles718,
            invocation.StartingLifestyleId718, invocation.SourceExecution832, invocation.CompiledMovement832,
            invocation.PhysicalAuthority886, invocation.PhysicalInitiative886,
            Consumer = invocation.TryGetCompletedConsumer("profile", out var consumer) ? consumer : null
        });

    internal static bool HasCurrentProfileInvocation3212(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string rawCharacterDigest)
        => profileInvocations3212.TryGetValue(invocation, out var bound)
            && ReferenceEquals(bound.Owner.profileInvocation3212, invocation)
            && bound.Owner.MatchesProfileOwner3212(invocation, bound.Authority)
            && bound.AuthorityDigest == CharacterCreationFinalizationDigest.Compute(bound.Authority)
            && bound.InvocationDigest == ProfileSourceInvocationDigest3212(invocation)
            && bound.Owner.Inputs.EffectivePolicy!.SettingsProfileId == profileId
            && bound.Owner.Inputs.EffectivePolicy.ProfileInputsDigest == profileInputsDigest
            && bound.ProfileXmlDigest == profileXmlDigest
            && bound.Owner.Inputs.Skills.Binding.RawCharacterXmlDigest == rawCharacterDigest;

    // Extend the existing funded loadout execution, not the observation vocabulary.
    // The same private invocation holds both each member receipt and its enclosing
    // selection. An empty list still executes the original AND compiled ware scope.
    private static void BindProfileLoadoutContainers3212(XElement profile, XElement originalGraph,
        XElement graph, CharacterCreationLoadoutCompletionProjection projected,
        Dictionary<string, string> results, CharacterCreationOriginalInventory3475? originalLoadout3478 = null,
        string? instanceSeed3497 = null)
    {
        if (profile.Name != "setting" || profile.HasAttributes) return;
        foreach (var (containerName, memberName) in new[]
            { ("bannedwaregrades", "grade"), ("redlinerexclusion", "limb") })
        {
            var containers = profile.Elements().Where(node => node.Name.LocalName == containerName).ToArray();
            if (containers is not [var container] || container.Name != containerName || container.HasAttributes
                || container.Nodes().Any(node => node is not XElement
                    && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))) continue;
            var members = container.Elements().ToArray();
            if (members.Any(node => node.Name != memberName || node.HasAttributes || node.HasElements
                    || node.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || string.IsNullOrWhiteSpace(node.Value) || node.Value.Length > 256
                    || node.Value.Any(char.IsControl))
                || members.Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() != members.Length
                // Every selected child must already have passed the existing exact
                // observation, source-value and no-ware consumer, including repeats.
                || members.Any(node => !results.ContainsKey(RelativePath1203(profile, node)))
                || !EvaluateLoadoutPredicateWithOriginal3497("no-ware-target", originalGraph, graph, projected,
                    out var mechanical, originalLoadout3478, instanceSeed3497)) continue;
            results.Add(RelativePath1203(profile, container), JsonSerializer.Serialize(new
            {
                Operation = "selected-profile-loadout-container3212", Container = containerName,
                XmlDigest = CharacterCreationFinalizationDigest.Compute(container.ToString(SaveOptions.DisableFormatting)),
                Members = members.Select(node => new
                {
                    Value = node.Value, Path = RelativePath1203(profile, node),
                    Execution = results[RelativePath1203(profile, node)]
                }).ToArray(),
                Predicate = "no-ware-target", Mechanical = mechanical,
                Projection = projected.ProjectionDigest
            }));
        }
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // A read-only identity gate. The retained resolver must still execute the
    // actual absence check; this does not consume any source operation itself.
    public bool HasCurrentProfileInvocation3212(string profileId, string profileInputsDigest,
        string profileXmlDigest, string rawCharacterDigest)
        => CharacterCreationCompleteRuleSourceExecution.HasCurrentProfileInvocation3212(this,
            profileId, profileInputsDigest, profileXmlDigest, rawCharacterDigest);
}
