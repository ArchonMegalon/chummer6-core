using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleGearRules
{
    internal static CharacterCreationLifeModuleGearQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        decimal totalKarma, IReadOnlyList<CharacterCreationGearSelection>? selections, ICharacterSourceDataContext context)
    {
        try
        {
            XElement root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            var existing = root.Elements("gears").ToArray();
            if (existing.Length > 1 || existing.Any(node => node.HasElements || node.HasAttributes || !string.IsNullOrWhiteSpace(node.Value)))
                return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);
            // Parent values are recalculated, never accepted solely because a
            // caller has recomputed a digest after changing its available cash.
            var funding = CharacterCreationLifeModuleResourcesRules.Evaluate(characterXml, effects, racial, talent,
                attributes, skills, totalKarma, resources.KarmaInvestment, context);
            if (funding.Quote is null || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resources, funding.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            if (!context.TryResolveCreationGearAuthority(out var authority))
                return Failed(CharacterCreationGearBlockers.AuthorityUnavailable);
            var result = Quote(authority, resources, selections);
            if (!context.TryResolveCreationGearAuthority(out var finalAuthority)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(authority, finalAuthority)
                || !context.TryResolveCreationLifeModuleResourcesPolicy(out var finalPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resources.Policy, finalPolicy)
                || !context.TryResolveCreationLifeModuleQualitiesPolicy(out var finalQualityPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resources.QualityCosts.Policy, finalQualityPolicy))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { return Failed(CharacterCreationGearBlockers.InvalidBasket); }
    }

    // Arithmetic projection of the already-admitted parent. It is not an
    // independent permission to spend or persist a caller-supplied quote.
    internal static CharacterCreationLifeModuleGearQuoteResult Quote(CharacterCreationGearAuthority authority,
        CharacterCreationLifeModuleResourcesQuote resources, IReadOnlyList<CharacterCreationGearSelection>? selections)
    {
        try
        {
            if (authority is null || resources is not { NuyenFromKarma: >= 0, Blockers: not null, Policy: not null }
                || !CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(resources.Policy, CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1)
                || resources.QuoteDigest != Hash(resources with { QuoteDigest = string.Empty })
                || resources.Policy.SettingsProfileId != authority.SettingsProfileId
                || resources.Policy.RawProfileInputsDigest != authority.ProfileDigest)
                return Failed(CharacterCreationGearBlockers.AuthorityUnavailable);
            if (selections is null)
                return CharacterCreationGearRules.IsValidAuthority(authority)
                    ? new(authority, null, [CharacterCreationLifeModuleGearQuote.SelectionRequired])
                    : Failed(CharacterCreationGearBlockers.AuthorityUnavailable);
            if (!CharacterCreationKarmaGearRules.TryFreeze(selections, out var frozen))
                return Failed(CharacterCreationGearBlockers.InvalidBasket);
            // Shared basket projection admits every catalog row, not only the
            // selected ones, and retains the exact source XML for finalization.
            CharacterCreationGearRules.TryProjectBasket(frozen, authority, resources.NuyenFromKarma,
                out var lines, out var budget, out var findings);
            if (findings.Contains(CharacterCreationGearBlockers.AuthorityUnavailable, StringComparer.Ordinal))
                return Failed(CharacterCreationGearBlockers.AuthorityUnavailable);
            var blockers = new HashSet<string>(findings.Concat(resources.Blockers), StringComparer.Ordinal);
            foreach (var line in lines)
                if (!CharacterCreationLegacySourceProjector.TryBuildGear(line, resources.QuoteDigest, out _))
                    blockers.Add(CharacterCreationGearBlockers.UnsupportedSemantics);
            var ordered = blockers.Order(StringComparer.Ordinal).ToArray();
            budget = budget with { IsExact = ordered.Length == 0, Blockers = ordered };
            var basis = new CharacterCreationKarmaGearBasis(authority.SettingsProfileId, authority.ProfileDigest,
                authority.SourceDigest, authority.RulesDigest, authority.RuntimeDigest, authority.AuthorityDigest,
                authority.MaximumAvailability, authority.MaximumBasketLines, authority.MaximumQuantityPerLine);
            var quote = new CharacterCreationLifeModuleGearQuote(basis, resources.QuoteDigest,
                frozen.OrderBy(row => row.OptionId, StringComparer.Ordinal).ToArray(), lines, budget, ordered, string.Empty);
            return new(authority, quote with { QuoteDigest = Hash(quote) }, ordered);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationGearBlockers.InvalidBasket); }
    }

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleGearQuoteResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleGearQuoteResult(CharacterCreationGearAuthority? Authority,
    CharacterCreationLifeModuleGearQuote? Quote, IReadOnlyList<string> Blockers);
