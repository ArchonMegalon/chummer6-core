using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleFinalizationBudgetRules
{
    internal static CharacterCreationLifeModuleFinalizationBudgetResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        CharacterCreationLifeModuleGearQuote gear, CharacterCreationLifeModuleLifestylesQuote lifestyles,
        CharacterCreationLifeModuleContactsQuote contacts, decimal totalKarma, int? diceTotal,
        ICharacterSourceDataContext context)
    {
        try
        {
            // Re-admit both branches against current sources. Neither a caller's
            // arithmetic nor a freshly recomputed hash establishes spending truth.
            var currentLifestyles = CharacterCreationLifeModuleLifestylesRules.Evaluate(characterXml, effects, racial,
                talent, attributes, skills, resources, gear, totalKarma, lifestyles.Selection, lifestyles.StartingLifestyleId, context);
            var currentContacts = CharacterCreationLifeModuleContactsRules.Evaluate(characterXml, effects, racial,
                talent, attributes, skills, resources, totalKarma, contacts.Selection, context);
            if (currentLifestyles.Quote is null || currentContacts.Quote is null
                || !Same(lifestyles, currentLifestyles.Quote) || !Same(contacts, currentContacts.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            if (!context.TryResolveCreationCarryoverPolicy(out var policy) || policy is null)
                return Failed(CharacterCreationLifeModuleFinalizationBudgetQuote.PolicyUnavailable);
            if (!TrySource(lifestyles, context, out var source) || source is null)
                return Failed(CharacterCreationLifeModuleFinalizationBudgetQuote.StartingCashUnavailable);
            var result = Quote(policy, source, resources, lifestyles, contacts, diceTotal);
            if (!context.TryResolveCreationCarryoverPolicy(out var finalPolicy) || !Same(policy, finalPolicy)
                || !TrySource(lifestyles, context, out var finalSource) || !Same(source, finalSource)
                || !context.TryResolveCreationContactsPolicy(out var finalContacts) || !Same(contacts.Policy, finalContacts)
                || !context.TryResolveCreationLifeModuleQualitiesPolicy(out var finalQualities) || !Same(resources.QualityCosts.Policy, finalQualities)
                || !context.TryResolveCreationLifeModuleResourcesPolicy(out var finalResources) || !Same(resources.Policy, finalResources)
                || !context.TryResolveCreationLifestylesAuthority(out var finalLifestyles)
                || !CharacterCreationLifeModuleLifestylesRules.TryEffectiveAuthority(finalLifestyles, effects, racial, talent, out var effective)
                || !Same(currentLifestyles.Authority, effective))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException or OverflowException)
        { return Failed(CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid); }
    }

    // Arithmetic over source-admitted quotes. Full semantic admission belongs to
    // Evaluate; no synthetic Karma foundation or random roll is used here.
    internal static CharacterCreationLifeModuleFinalizationBudgetResult Quote(CharacterCreationKarmaCarryoverPolicy policy,
        CharacterCreationStartingNuyenSource source, CharacterCreationLifeModuleResourcesQuote resources,
        CharacterCreationLifeModuleLifestylesQuote lifestyles, CharacterCreationLifeModuleContactsQuote contacts, int? diceTotal)
    {
        try
        {
            if (!CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(policy)
                || !CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(source)
                || resources is not { Policy: not null, Blockers.Count: 0 }
                || lifestyles is not { Blockers.Count: 0, Budget: { IsExact: true, Remaining: >= 0 } }
                || contacts is not { Blockers.Count: 0 }
                || resources.QuoteDigest != Hash(resources with { QuoteDigest = string.Empty })
                || lifestyles.QuoteDigest != Hash(lifestyles with { QuoteDigest = string.Empty })
                || contacts.QuoteDigest != Hash(contacts with { QuoteDigest = string.Empty })
                || !CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(resources.Policy, CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1)
                || source.SettingsProfileId != policy.SettingsProfileId || source.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || resources.Policy.SettingsProfileId != policy.SettingsProfileId || resources.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || lifestyles.ResourcesQuoteDigest != resources.QuoteDigest || contacts.ResourcesQuoteDigest != resources.QuoteDigest
                || contacts.QualityCostsQuoteDigest != resources.QualityCosts.QuoteDigest
                || contacts.KarmaBeforeContacts != resources.KarmaAfterResources
                || contacts.KarmaAfterContacts != resources.KarmaAfterResources - contacts.KarmaUsed
                || !MatchesStartingLifestyle(source, lifestyles))
                return Failed(CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid);
            if (diceTotal is null)
                return new(policy, source, null, [CharacterCreationLifeModuleFinalizationBudgetQuote.DiceRequired]);
            if (diceTotal < source.Dice || diceTotal > checked(source.Dice * 6))
                return new(policy, source, null, [CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid]);

            decimal adjustment = decimal.Ceiling(resources.KarmaInvestment) - resources.KarmaInvestment;
            decimal remainingKarma = contacts.KarmaAfterContacts - adjustment;
            if (remainingKarma < 0 || remainingKarma > int.MaxValue || decimal.Truncate(remainingKarma) != remainingKarma)
                return new(policy, source, null, [CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid]);
            int karma = (int)remainingKarma;
            int carriedKarma = Math.Min(karma, policy.MaximumKarma);
            decimal available = lifestyles.Budget.Remaining;
            decimal carriedNuyen = Math.Min(available, policy.MaximumNuyen);
            decimal starting = checked(diceTotal.Value * source.Multiplier);
            var quote = new CharacterCreationLifeModuleFinalizationBudgetQuote(policy, source, resources.QuoteDigest,
                lifestyles.QuoteDigest, contacts.QuoteDigest, diceTotal.Value, adjustment, karma, carriedKarma, karma - carriedKarma,
                available, carriedNuyen, available - carriedNuyen, starting, checked(carriedNuyen + starting),
                policy.SourceAnchorIds.Concat(source.SourceAnchorIds).Concat(lifestyles.Budget.SourceAnchorIds)
                    .Append(CharacterCreationKarmaFinalizationBudgetRules.ResourceRoundingAnchor)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return new(policy, source, quote with { QuoteDigest = Hash(quote) }, []);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid); }
    }

    private static bool TrySource(CharacterCreationLifeModuleLifestylesQuote lifestyles, ICharacterSourceDataContext context,
        out CharacterCreationStartingNuyenSource? source)
    {
        source = null;
        if (lifestyles.Selection.Count == 0 && lifestyles.Lines.Count == 0 && lifestyles.StartingLifestyleId is null)
            return context.TryResolveCreationDefaultStartingNuyen(out source);
        var selected = lifestyles.Lines.SingleOrDefault(row => row.Configuration.LifestyleId == lifestyles.StartingLifestyleId);
        return selected is not null && context.TryResolveCreationLifeModuleStartingNuyen(selected.SourceId, out source);
    }

    private static bool MatchesStartingLifestyle(CharacterCreationStartingNuyenSource source, CharacterCreationLifeModuleLifestylesQuote lifestyles)
    {
        if (source.SourceInputsDigest != lifestyles.ProjectionAuthority.SourceDigest) return false;
        if (lifestyles.Selection.Count == 0 && lifestyles.Lines.Count == 0 && lifestyles.StartingLifestyleId is null)
        {
            var costs = XElement.Parse(source.SourceNodeXml).Elements("cost").ToArray();
            return source.Name == "Street" && costs.Length == 1 && !costs[0].HasAttributes && !costs[0].HasElements
                && decimal.TryParse(costs[0].Value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal cost) && cost == 0m;
        }
        var selected = lifestyles.Lines.SingleOrDefault(row => row.Configuration.LifestyleId == lifestyles.StartingLifestyleId);
        var option = lifestyles.ProjectionAuthority.LifestyleOptions.SingleOrDefault(row => row.SourceId == selected?.SourceId);
        return option is not null && source.SourceId == option.SourceId.ToString("D") && source.Name == option.Name
            && source.SourceBook == option.SourceBook && source.Page == option.Page
            && source.Dice == option.StartingNuyenDice && source.Multiplier == option.StartingNuyenMultiplier;
    }

    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleFinalizationBudgetResult Failed(string blocker) => new(null, null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleFinalizationBudgetResult(CharacterCreationKarmaCarryoverPolicy? Policy,
    CharacterCreationStartingNuyenSource? StartingCashSource, CharacterCreationLifeModuleFinalizationBudgetQuote? Quote,
    IReadOnlyList<string> Blockers);
