using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleContactsRules
{
    internal static CharacterCreationLifeModuleContactsQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        decimal totalKarma, IReadOnlyList<CharacterCreationKarmaContactSelection>? selections, ICharacterSourceDataContext context)
    {
        try
        {
            var root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            var existing = root.Elements("contacts").ToArray();
            if (existing.Length > 1 || existing.Any(row => row.HasElements || row.HasAttributes || !string.IsNullOrWhiteSpace(row.Value)))
                return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);
            var funding = CharacterCreationLifeModuleResourcesRules.Evaluate(characterXml, effects, racial, talent,
                attributes, skills, totalKarma, resources.KarmaInvestment, context);
            if (funding.Quote is null || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resources, funding.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            if (!context.TryResolveCreationContactsPolicy(out var policy) || policy is null)
                return Failed(CharacterCreationContactsBlockers.AuthorityUnavailable);
            var result = Quote(effects, racial, talent, attributes, resources, policy, selections);
            if (!context.TryResolveCreationContactsPolicy(out var finalPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, finalPolicy)
                || !context.TryResolveCreationLifeModuleQualitiesPolicy(out var qualityPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resources.QualityCosts.Policy, qualityPolicy))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { return Failed(CharacterCreationContactsBlockers.ContactInvalid); }
    }

    // Arithmetic over source-admitted plans. Evaluate replays current funding
    // before calling this; a self-consistent caller hash is never spending authority.
    internal static CharacterCreationLifeModuleContactsQuoteResult Quote(
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleResourcesQuote resources, CharacterCreationKarmaContactsPolicy policy,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? selections)
    {
        try
        {
            if (!CharacterCreationKarmaContactsRules.IsValidPolicy(policy)
                || resources is not { QualityCosts: { } quality, Policy: not null, Blockers: not null }
                || !CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(resources.Policy, CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1)
                || resources.QuoteDigest != Hash(resources with { QuoteDigest = string.Empty })
                || attributes.QuoteDigest != Hash(attributes with { QuoteDigest = string.Empty })
                || resources.AttributeQuoteDigest != attributes.QuoteDigest
                || resources.EffectPlanDigest != effects.PlanDigest || resources.MetatypePlanDigest != racial.PlanDigest
                || resources.TalentPlanDigest != talent.PlanDigest || attributes.EffectPlanDigest != effects.PlanDigest
                || attributes.MetatypePlanDigest != racial.PlanDigest || attributes.TalentPlanDigest != talent.PlanDigest
                || policy.SettingsProfileId != resources.Policy.SettingsProfileId
                || policy.RawProfileInputsDigest != resources.Policy.RawProfileInputsDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quality,
                    CharacterCreationLifeModuleQualityCostsRules.Quote(effects, racial, talent, quality.Policy))
                || !CharacterCreationKarmaContactsRules.TryContactPoints(policy,
                    attributes.Attributes.ToDictionary(row => row.AttributeId, row => row.Current), out int points))
                return Failed(CharacterCreationContactsBlockers.AuthorityUnavailable);
            if (selections is null)
                return new(policy, null, [CharacterCreationLifeModuleContactsQuote.SelectionRequired]);
            if (!CharacterCreationKarmaContactsRules.TryFreeze(selections, out var frozen))
                return new(policy, null, [CharacterCreationContactsBlockers.ContactInvalid]);

            var root = new XElement("character", new XElement("created", false),
                new XElement("buildmethod", CharacterCreationBuildMethods.LifeModules), new XElement("settings", policy.SettingsProfileId),
                new XElement("contactpoints", points), new XElement("attributes", attributes.Attributes.Select(row =>
                    new XElement("attribute", new XElement("name", row.AttributeId), new XElement("totalvalue", row.Current)))),
                new XElement("improvements", effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent.ImprovementXml)
                    .Select(xml => XElement.Parse(xml))),
                new XElement("contacts", frozen.Select(CharacterCreationKarmaContactsRules.BuildContactElement)));
            var authority = CharacterCreationContactsAuthorityEvaluator.Evaluate(
                new WorkspaceDocument(root.ToString(SaveOptions.DisableFormatting), RulesetDefaults.Sr5));
            if (authority.AuthorityBlockers.Count != 0 || !authority.ContactBudget.IsExact || !authority.HighPlacesBudget.IsExact)
                return new(policy, null, [CharacterCreationContactsBlockers.AuthorityUnavailable]);
            var lines = new List<CharacterCreationKarmaContactLine>();
            foreach (var choice in frozen)
            {
                if (!CharacterContactEditSemanticsResolver.TryResolve(root, CharacterCreationKarmaContactsRules.BuildContactElement(choice), out var semantics)
                    || semantics.Connection != choice.Connection || semantics.Loyalty != choice.Loyalty
                    || semantics.IsGroup != choice.IsGroup || semantics.Free != choice.Free
                    || semantics.Family != choice.Family || semantics.Blackmail != choice.Blackmail)
                    return new(policy, null, [CharacterCreationContactsBlockers.ContactInvalid]);
                var cost = authority.Contacts.Single(row => row.ContactId == choice.ContactId);
                lines.Add(new(choice, cost.ContactPointCost, cost.CountsAgainstHighPlacesBudget));
            }
            int groupKarma = checked(lines.Where(row => row.Selection.IsGroup && !row.Selection.Free)
                .Sum(row => row.PointCost) * policy.GroupContactKarmaMultiplier);
            if (!CharacterCreationQualityCostRules.TryCalculate(quality.Policy.Costs, quality.Policy.QualityKarmaLimit,
                quality.Lines.Select(row => new CharacterCreationQualityCostItem(row.SourceKarma, row.CountsAgainstQualityLimit,
                    row.CountsAgainstKarma, row.CountsAgainstMetagenicLimit)).ToArray(), groupKarma,
                quality.FreePositiveQualities, quality.FreeNegativeQualities, out var combined))
                return Failed(CharacterCreationContactsBlockers.AuthorityUnavailable);
            int additionalQuality = checked(combined.NetKarmaSpent - quality.Costs.NetKarmaSpent);
            int used = checked(authority.ContactBudget.Overspend + authority.HighPlacesBudget.Overspend + additionalQuality);
            if (additionalQuality < 0 || used < 0) return Failed(CharacterCreationContactsBlockers.AuthorityUnavailable);
            var blockers = new HashSet<string>(resources.Blockers, StringComparer.Ordinal);
            if (combined.PositiveLimitKarma > quality.Policy.QualityKarmaLimit && !quality.Policy.MayExceedPositiveLimit)
                blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
            bool friendsInHighPlaces = root.Element("improvements")!.Elements("improvement")
                .Any(row => row.Element("improvementttype")?.Value == "FriendsInHighPlaces");
            if (lines.Any(row => (!friendsInHighPlaces || row.Selection.Connection < 8) && row.PointCost > 7)
                || friendsInHighPlaces && lines.Where(row => row.Selection.Connection >= 8 && row.PointCost > 7)
                    .Sum(row => row.PointCost) > authority.HighPlacesBudget.Total)
                blockers.Add(CharacterCreationLifeModuleContactsQuote.ContactLimitExceeded);
            if (used > resources.KarmaAfterResources) blockers.Add(CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
            var quote = new CharacterCreationLifeModuleContactsQuote(policy, resources.QuoteDigest, attributes.QuoteDigest,
                quality.QuoteDigest, authority.SourceDigest, authority.RulesDigest, authority.RuntimeDigest,
                frozen, lines.ToArray(), authority.ContactBudget, authority.HighPlacesBudget, groupKarma, combined,
                additionalQuality, used, resources.KarmaAfterResources, checked(resources.KarmaAfterResources - used),
                blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return new(policy, quote with { QuoteDigest = Hash(quote) }, quote.Blockers);
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException or OverflowException)
        { return Failed(CharacterCreationContactsBlockers.ContactInvalid); }
    }

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleContactsQuoteResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleContactsQuoteResult(CharacterCreationKarmaContactsPolicy? Policy,
    CharacterCreationLifeModuleContactsQuote? Quote, IReadOnlyList<string> Blockers);
