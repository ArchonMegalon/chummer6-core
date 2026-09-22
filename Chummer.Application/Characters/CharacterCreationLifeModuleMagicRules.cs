using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationLifeModuleMagicRules
{
    public static string ComputeCatalogDigest(CharacterCreationLifeModuleMagicCatalog catalog)
        => Hash(catalog with { AuthorityDigest = string.Empty });

    internal static CharacterCreationLifeModuleMagicResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        CharacterCreationLifeModuleContactsQuote contacts, decimal totalKarma,
        CharacterCreationMagicResonanceSelections? selections, ICharacterSourceDataContext context)
    {
        try
        {
            var root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            foreach (string name in new[] { "tradition", "stream", "spells", "powers", "complexforms" })
            {
                var rows = root.Elements(name).ToArray();
                if (rows.Length > 1 || rows.Any(row => row.HasElements || row.HasAttributes || !string.IsNullOrWhiteSpace(row.Value)))
                    return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);
            }
            var currentTalent = CharacterCreationLifeModuleTalentWritePlanner.Build(characterXml, effects, racial, talent.Selection, context);
            var currentContacts = CharacterCreationLifeModuleContactsRules.Evaluate(characterXml, effects, racial, talent,
                attributes, skills, resources, totalKarma, contacts.Selection, context);
            if (currentTalent.Plan is null || !Same(talent, currentTalent.Plan)
                || currentContacts.Quote is null || !Same(contacts, currentContacts.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            if (!context.TryResolveCreationLifeModuleMagicCatalog(out var catalog) || catalog is null)
                return Failed(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
            var result = Quote(catalog, effects, racial, talent, attributes, skills, resources, contacts, selections);
            if (!context.TryResolveCreationLifeModuleMagicCatalog(out var finalCatalog) || !Same(catalog, finalCatalog)
                || !context.TryResolveCreationContactsPolicy(out var finalContacts) || !Same(contacts.Policy, finalContacts)
                || !context.TryResolveCreationLifeModuleQualitiesPolicy(out var finalQualities) || !Same(resources.QualityCosts.Policy, finalQualities))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationMagicResonanceBlockers.OptionInvalid); }
    }

    internal static CharacterCreationLifeModuleMagicResult Quote(CharacterCreationLifeModuleMagicCatalog catalog,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationLifeModuleResourcesQuote resources,
        CharacterCreationLifeModuleContactsQuote contacts, CharacterCreationMagicResonanceSelections? selections)
    {
        try
        {
            if (!IsValidCatalog(catalog) || !Same(catalog.Talents, talent.Catalog)
                || effects.PlanDigest != Hash(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != Hash(racial with { PlanDigest = string.Empty })
                || talent.PlanDigest != Hash(talent with { PlanDigest = string.Empty })
                || attributes.QuoteDigest != Hash(attributes with { QuoteDigest = string.Empty })
                || skills.QuoteDigest != Hash(skills with { QuoteDigest = string.Empty })
                || resources.QuoteDigest != Hash(resources with { QuoteDigest = string.Empty })
                || contacts.QuoteDigest != Hash(contacts with { QuoteDigest = string.Empty })
                || resources.EffectPlanDigest != effects.PlanDigest || resources.MetatypePlanDigest != racial.PlanDigest
                || resources.TalentPlanDigest != talent.PlanDigest || resources.AttributeQuoteDigest != attributes.QuoteDigest
                || resources.SkillsQuoteDigest != skills.QuoteDigest || contacts.ResourcesQuoteDigest != resources.QuoteDigest
                || contacts.AttributesQuoteDigest != attributes.QuoteDigest
                || catalog.SettingsProfileId != resources.Policy.SettingsProfileId
                || catalog.RawProfileInputsDigest != resources.Policy.RawProfileInputsDigest
                || !Same(talent.Talent, catalog.Talents.Options.SingleOrDefault(row => row.OptionId == talent.Selection.OptionId))
                || !catalog.Talents.SkillUnlockChoices.TryGetValue(talent.Selection.OptionId, out var unlocks)
                || skills.Selection.TalentUnlock != talent.Selection.SkillUnlock)
                return Failed(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
            if (selections is null) return new(catalog, null, [CharacterCreationLifeModuleMagicQuote.SelectionRequired]);
            var magic = attributes.Attributes.SingleOrDefault(row => row.AttributeId == "MAG");
            var resonance = attributes.Attributes.SingleOrDefault(row => row.AttributeId == "RES");
            if (talent.Talent.EnabledAttribute == "MAG" && magic is null
                || talent.Talent.EnabledAttribute == "RES" && resonance is null)
                return Failed(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
            var improvements = effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent.ImprovementXml)
                .Select(xml => XElement.Parse(xml)).ToArray();
            var purchase = CharacterCreationMagicPurchaseRules.Evaluate(catalog.Policy, catalog.Catalogs, talent.Talent,
                // A single source unlock is automatic (for example Magician).
                // Only an explicit choice among aspects restricts spell access.
                unlocks.Count > 1, talent.Selection.SkillUnlock,
                magic?.Current ?? 0, resonance?.Current ?? 0,
                improvements, selections, contacts.KarmaAfterContacts, true);
            if (purchase is null) return new(catalog, null, [CharacterCreationMagicResonanceBlockers.OptionInvalid]);
            var identities = purchase.Sources.Select(row => row.Identity).ToHashSet();
            var subset = catalog with
            {
                Catalogs = catalog.Catalogs.Select(slice => slice with
                    { Options = slice.Options.Where(row => identities.Contains(row.Identity)).ToArray() }).ToArray(),
                AuthorityDigest = string.Empty
            };
            subset = subset with { AuthorityDigest = Hash(subset) };
            var blockers = contacts.Blockers.Concat(purchase.Blockers).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var quote = new CharacterCreationLifeModuleMagicQuote(catalog.AuthorityDigest, subset, talent.PlanDigest,
                attributes.QuoteDigest, skills.QuoteDigest, resources.QuoteDigest, contacts.QuoteDigest,
                purchase.TalentKind, purchase.Access, purchase.Selections, purchase.Sources, purchase.Cost,
                purchase.PowerPointsTotal, purchase.PowerPointsUsed, purchase.MaximumSpellsPerKind, purchase.MaximumComplexForms,
                contacts.KarmaAfterContacts, checked(contacts.KarmaAfterContacts - purchase.Cost.TotalKarma), blockers, string.Empty);
            return new(catalog, quote with { QuoteDigest = Hash(quote) }, blockers);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationMagicResonanceBlockers.OptionInvalid); }
    }

    internal static bool IsValidCatalog(CharacterCreationLifeModuleMagicCatalog? catalog)
    {
        string[] kinds = ["tradition", "stream", "adept-power", "spell", "complex-form"];
        return catalog is { Policy: not null, Talents: { Options: not null, SkillUnlockChoices: not null }, Catalogs.Count: 5, SourceAnchorIds.Count: > 0 }
            && CharacterCreationKarmaMagicRules.IsValidLifeModulePolicy(catalog.Policy)
            && catalog.Talents.Schema == CharacterCreationLifeModuleTalentCatalog.SchemaV1
            && catalog.SettingsProfileId == catalog.Policy.SettingsProfileId && catalog.SettingsProfileId == catalog.Talents.SettingsProfileId
            && catalog.RawProfileInputsDigest == catalog.Talents.RawProfileInputsDigest
            && CharacterCreationMagicResonanceDigest.IsCanonical(catalog.RawProfileInputsDigest)
            && CharacterCreationMagicResonanceDigest.IsCanonical(catalog.CustomDataInputsDigest)
            && catalog.Talents.AuthorityDigest == Hash(catalog.Talents with { AuthorityDigest = string.Empty })
            && catalog.Catalogs.All(slice => slice is not null && slice.Options is not null
                && CharacterCreationMagicResonanceDigest.IsCanonical(slice.EffectiveSourceDigest)
                && slice.Options.All(row => row?.Identity is { } id && id.Kind == slice.Kind
                    && Guid.TryParseExact(id.SourceId, "D", out var guid) && guid != Guid.Empty && guid.ToString("D") == id.SourceId)
                && slice.Options.Select(row => row.Identity).Distinct().Count() == slice.Options.Count)
            && catalog.Catalogs.Select(slice => slice.Kind).Order(StringComparer.Ordinal).SequenceEqual(kinds.Order(StringComparer.Ordinal))
            && catalog.AuthorityDigest == Hash(catalog with { AuthorityDigest = string.Empty });
    }

    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleMagicResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleMagicResult(CharacterCreationLifeModuleMagicCatalog? Catalog,
    CharacterCreationLifeModuleMagicQuote? Quote, IReadOnlyList<string> Blockers);
