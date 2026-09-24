using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleLifestylesRules
{
    internal static CharacterCreationLifeModuleLifestylesQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        CharacterCreationLifeModuleGearQuote gear, decimal totalKarma,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? selections, Guid? startingLifestyleId,
        ICharacterSourceDataContext context)
    {
        try
        {
            XElement root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            var existing = root.Elements("lifestyles").ToArray();
            if (existing.Length > 1 || existing.Any(node => node.HasElements || node.HasAttributes || !string.IsNullOrWhiteSpace(node.Value)))
                return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);
            var currentGear = CharacterCreationLifeModuleGearRules.Evaluate(characterXml, effects, racial, talent,
                attributes, skills, resources, totalKarma, gear.Selection, context);
            if (currentGear.Quote is null || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(gear, currentGear.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            if (!context.TryResolveCreationLifestylesAuthority(out var authority)
                || !TryEffectiveAuthority(authority, effects, racial, talent, out var effective))
                return Failed(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            var result = Quote(effective, authority.AuthorityDigest, resources, gear, selections, startingLifestyleId);
            if (!context.TryResolveCreationLifestylesAuthority(out var finalAuthority)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(authority, finalAuthority))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { return Failed(CharacterCreationLifestylesBlockers.InvalidMutation); }
    }

    internal static bool TryEffectiveAuthority(CharacterCreationLifestylesAuthority authority,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, out CharacterCreationLifestylesAuthority effective)
    {
        effective = CharacterCreationLifestylesAuthority.Unavailable;
        try
        {
            if (!CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || effects.PlanDigest != Hash(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != Hash(racial with { PlanDigest = string.Empty })
                || talent.PlanDigest != Hash(talent with { PlanDigest = string.Empty })
                || racial.EffectPlanDigest != effects.PlanDigest || talent.EffectPlanDigest != effects.PlanDigest
                || talent.MetatypePlanDigest != racial.PlanDigest
                || !CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts(
                    effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent.ImprovementXml).Select(xml => XElement.Parse(xml)),
                    authority.TrustFundLevel, out int trust, out decimal racialPercent, out _)) return false;
            var result = authority with
            {
                TrustFundLevel = trust, MetatypeCostPercent = checked(authority.MetatypeCostPercent + racialPercent),
                GmPolicyDigest = Hash(new { authority.GmPolicyDigest, Effects = effects.PlanDigest,
                    Racial = racial.PlanDigest, Talent = talent.PlanDigest, TrustFundLevel = trust, MetatypeCostPercent = racialPercent }),
                AuthorityDigest = string.Empty
            };
            effective = result with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(result) };
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return false; }
    }

    // Projects source-admitted values. Evaluate above rechecks the parent gear
    // and funding semantics, not just their hashes, before calling this method.
    internal static CharacterCreationLifeModuleLifestylesQuoteResult Quote(CharacterCreationLifestylesAuthority authority,
        string sourceAuthorityDigest, CharacterCreationLifeModuleResourcesQuote resources, CharacterCreationLifeModuleGearQuote gear,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? selections, Guid? startingLifestyleId)
    {
        try
        {
            if (!CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(sourceAuthorityDigest)
                || resources?.Policy is null || gear is not { Blockers: not null, Selection: not null }
                || !CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(resources.Policy, CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1)
                || authority.SettingsProfileId != resources.Policy.SettingsProfileId
                || authority.ProfileDigest != resources.Policy.RawProfileInputsDigest
                || resources.QuoteDigest != Hash(resources with { QuoteDigest = string.Empty })
                || gear.QuoteDigest != Hash(gear with { QuoteDigest = string.Empty })
                || gear.ResourcesQuoteDigest != resources.QuoteDigest)
                return Failed(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            if (selections is null)
                return new(authority, null, [CharacterCreationLifeModuleLifestylesQuote.SelectionRequired]);
            if (!CharacterCreationKarmaLifestylesRules.TryFreeze(selections, out var frozen))
                return Failed(CharacterCreationLifestylesBlockers.InvalidMutation);
            var lines = new List<CharacterCreationLifestyleProjection>();
            decimal cost = 0m;
            foreach (var choice in frozen)
            {
                if (!CharacterCreationLifestylesRules.TryProject(choice, authority, out var line, out var failures))
                    return new(authority, null, failures);
                lines.Add(line);
                cost = checked(cost + line.Economics.TotalCost);
            }
            var blockers = new HashSet<string>(gear.Blockers.Concat(resources.Blockers), StringComparer.Ordinal);
            if (frozen.Length == 0 ? startingLifestyleId is not null
                : startingLifestyleId is null || frozen.All(row => row.LifestyleId != startingLifestyleId))
                blockers.Add(CharacterCreationLifeModuleLifestylesQuote.StartingLifestyleRequired);
            decimal used = checked(gear.Budget.BasketCost + cost);
            decimal remaining = checked(resources.NuyenFromKarma - used);
            if (remaining < 0) blockers.Add(CharacterCreationLifestylesBlockers.InsufficientFunds);
            var ordered = blockers.Order(StringComparer.Ordinal).ToArray();
            var anchors = authority.SourceAnchorIds.Concat(lines.SelectMany(line => line.SourceAnchorIds))
                .Concat(gear.Lines.SelectMany(line => line.SourceAnchorIds)).Concat(resources.Policy.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var budget = new CharacterCreationLifestyleBudget(resources.NuyenFromKarma, used, remaining,
                Math.Max(0m, -remaining), ordered.Length == 0, ordered, anchors);
            var quote = new CharacterCreationLifeModuleLifestylesQuote(sourceAuthorityDigest,
                CharacterCreationKarmaLifestylesRules.ProjectionAuthority(authority, frozen), resources.QuoteDigest,
                gear.QuoteDigest, frozen, lines.ToArray(), startingLifestyleId, cost, budget, ordered, string.Empty);
            return new(authority, quote with { QuoteDigest = Hash(quote) }, ordered);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationLifestylesBlockers.InvalidMutation); }
    }

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleLifestylesQuoteResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleLifestylesQuoteResult(CharacterCreationLifestylesAuthority? Authority,
    CharacterCreationLifeModuleLifestylesQuote? Quote, IReadOnlyList<string> Blockers);
