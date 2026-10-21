using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationAttributePolicyAuthority
{
    // The live profile resolver and Complete must interpret the same source rule.
    // An explicit primary setting wins even over an invalid legacy setting.
    // Null means malformed/ambiguous authority; absence is the existing default.
    public static int? ResolveMaximumAttributesAtCap(XElement settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        XElement[] maximumNodes = settings.Elements("maxnumbermaxattributescreate").Take(2).ToArray();
        if (maximumNodes.Length != 0)
            return maximumNodes.Length == 1 && TryParseNonNegativeIntElement(maximumNodes[0], out int maximum)
                ? maximum : null;
        XElement[] legacyNodes = settings.Elements("allow2ndmaxattribute").Take(2).ToArray();
        if (legacyNodes.Length == 0) return 1;
        return legacyNodes.Length == 1 && TryParseStrictBoolElement(legacyNodes[0], out bool allowSecond)
            ? allowSecond ? 2 : 1 : null;
    }

    private static bool TryParseStrictBoolElement(XElement element, out bool value)
    {
        value = false;
        return !element.HasAttributes && !element.HasElements
            && string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal)
            && bool.TryParse(element.Value, out value);
    }

    private static bool TryParseNonNegativeIntElement(XElement element, out int value)
    {
        value = 0;
        return !element.HasAttributes && !element.HasElements
            && string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal)
            && int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }

    // Policy/state agreement and evaluation alone do not establish source authority.
    // Selected replay also binds these flags to its independently validated prerequisite.
    internal static bool MatchesSourceHouseRules(CharacterCreationAttributePolicy? policy,
        CharacterCreationAttributesState state, CharacterCreationPrerequisiteAuthority source)
        => source.AlternateMetatypeAttributeKarma is { } alternate
            && source.ReverseAttributePriorityOrder is { } reverse
            && (state.HouseRules?.AlternateMetatypeAttributeKarma ?? false) == alternate
            && (state.HouseRules?.ReverseAttributePriorityOrder ?? false) == reverse
            && MatchesHouseRules(policy, state);

    // Validate the versioned input against the policy and common evaluator.
    // Both are mutable evidence until MatchesSourceHouseRules binds them to source.
    internal static bool MatchesHouseRules(CharacterCreationAttributePolicy? policy,
        CharacterCreationAttributesState state)
    {
        if (state.HouseRules is not { } rules)
            return policy is null || !policy.AlternateMetatypeAttributeKarma && !policy.ReverseAttributePriorityOrder;
        if (!rules.IsValid || policy is null
            || rules.AlternateMetatypeAttributeKarma != policy.AlternateMetatypeAttributeKarma
            || rules.ReverseAttributePriorityOrder != policy.ReverseAttributePriorityOrder)
            return false;
        var session = CharacterCreationAttributesService.CreateDraftSession(state);
        var original = session?.Preview(state.Attributes.Select(row =>
            new CharacterCreationAttributeAllocation(row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray()).Value;
        return original is not null
            && original.Blockers.All(value => value == CharacterCreationAttributesBlockers.DraftDuplicate)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(original.Attributes, state.Attributes)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(original.NormalPointBudget, state.NormalPointBudget)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(original.SpecialPointBudget, state.SpecialPointBudget)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(original.CreationKarmaBudget, state.CreationKarmaBudget);
    }

    public static string ComputeDigest(CharacterCreationAttributePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            policy with { AuthorityDigest = string.Empty });
    }
}
