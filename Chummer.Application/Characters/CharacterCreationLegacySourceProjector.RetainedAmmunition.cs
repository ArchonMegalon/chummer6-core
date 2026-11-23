using System.Collections.Frozen;
using System.Xml.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // Pure category membership for one exact privately validated loadout. This
    // object lives only in its issuing retained evaluation, never on a source,
    // caller DTO or global cache. It grants no source/current/admission authority.
    internal sealed class RetainedAmmunitionCategories7961
    {
        private readonly CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218 validated;
        private readonly Dictionary<string, FrozenSet<string>> byType = new(StringComparer.Ordinal);

        internal RetainedAmmunitionCategories7961(CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218 validated)
            => this.validated = validated;

        internal bool Matches(CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218 candidate)
            => ReferenceEquals(validated, candidate);

        internal FrozenSet<string> ForType(string type)
        {
            // The finite physical-round whitelist bounds the number of entries.
            // Do not retain empty results or partial scans after grammar failures.
            if (!SessionPlayWeaponAuthority.IsPhysicalRoundType3483(type)) return FrozenSet<string>.Empty;
            if (byType.TryGetValue(type, out var categories)) return categories;
            categories = validated.Authority.Options.Where(option => IsCareerAmmunitionCategory(option, type))
                .Select(option => option.Category).ToFrozenSet(StringComparer.Ordinal);
            if (categories.Count > 0) byType.Add(type, categories);
            return categories;
        }
    }

    // Gear.Create chooses Extra from weapons.xml categories for ammunition. A saved
    // Windows choice is inventory state, not a request to run the purchase editor.
    // Retained admission and new purchases share the category grammar, pricing
    // evaluator and Gear.Save writer; only purchases require a typed selection.
    internal sealed class RetainedCareerAmmunition
    {
        private readonly XElement source;
        private readonly IReadOnlySet<string> categories;
        internal string WeaponAuthorityDigest { get; }

        internal RetainedCareerAmmunition(XElement source, IReadOnlySet<string> categories, string digest)
        { this.source = source; this.categories = categories; WeaponAuthorityDigest = digest; }

        internal bool ValidateCatalog(CharacterCareerGearPurchaseRequest probe, string settings)
            => ValidateCatalogCore8012(probe, settings, null);

        internal bool ValidateCatalog(CharacterCareerGearPurchaseRequest probe, CareerGearPricePolicyEvaluation policy)
            => ValidateCatalogCore8012(probe, null, policy);

        private bool ValidateCatalogCore8012(CharacterCareerGearPurchaseRequest probe, string? settings,
            CareerGearPricePolicyEvaluation? policy)
        {
            if (!TryReadCareerGearRatings(source, out int minimum, out int maximum, out _, out _, out _)
                || maximum > MaximumCareerGearCatalogRatingChoices) return false;
            for (int rating = minimum; rating <= maximum; rating++)
            {
                var request = probe with { Rating = rating };
                bool valid = policy is null
                    ? TryBuildCareerGearFromSource(source, request, settings, null,
                        probe.SourceId, out _, out _, out _, out _, out _, out _)
                    : policy.TryBuildPurchasedGearFromSource(source, request,
                        probe.SourceId, out _, out _, out _, out _, out _, out _);
                if (!valid) return false;
            }
            return true;
        }

        internal bool TryProjectSaved(XElement saved, CharacterCareerGearPurchaseRequest request,
            string settings, Guid instance, out string payload, out decimal unit, out decimal total,
            out int availability, out string legality)
        {
            payload = legality = string.Empty; unit = total = 0m; availability = 0;
            if (instance == Guid.Empty || saved.HasAttributes
                || saved.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
                || !Guid.TryParse(ReadOptionalScalar(saved, "sourceid"), out var id) || id != request.SourceId
                || !Guid.TryParse(ReadOptionalScalar(source, "id"), out var sourceId) || id != sourceId
                || ReadOptionalScalar(saved, "extra") is not { Length: > 0 } category
                || !categories.Contains(category)
                || ReadOptionalScalar(saved, "ammoforweapontype") != ReadOptionalScalar(source, "ammoforweapontype")
                || request.Quantity < 0m || request.Quantity != decimal.Truncate(request.Quantity)
                || !TryBuildCareerGearFromSource(source,
                    request.Quantity == 0m ? request with { Quantity = 1m } : request, settings, null, instance,
                    out payload, out unit, out total, out availability, out legality, out _)) return false;

            var projected = XElement.Parse(payload);
            projected.Element("extra")!.Value = category;
            // Settlement retains empty stacks. Purchase deliberately requires a
            // positive amount, so use its unit projection for source validation,
            // then retain the actual zero inventory and its zero replacement total.
            if (request.Quantity == 0m)
            { projected.Element("qty")!.Value = "0"; total = 0m; }
            // Saved historical price/availability are not current replacement values.
            // Physical type, unit and effects must agree with the authoritative source.
            foreach (string field in new[] { "name", "category", "source", "costfor", "isflechetteammo",
                "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
            {
                var actual = saved.Element(field);
                var expected = projected.Element(field);
                if (expected is null ? actual is not null : actual is null || !XNode.DeepEquals(actual, expected)) return false;
            }
            // Gear.Save spells this discountedcost (older consumer also checks discountcost).
            if (saved.Element("discountedcost") is { } discount
                && (ReadOptionalScalar(saved, "discountedcost") is not { } flag
                    || !bool.TryParse(flag, out bool discounted) || discounted)) return false;
            payload = projected.ToString(SaveOptions.DisableFormatting);
            return true;
        }
    }

    internal static bool TryReadRetainedCareerAmmunition(CharacterCareerGearPurchaseSource authority,
        CharacterCreationLoadoutAuthority loadout, out RetainedCareerAmmunition? ammunition)
    {
        ammunition = null;
        return loadout.RulesetId == "sr5" && CharacterCreationLoadoutRules.IsValidAuthority(loadout)
            && TryReadRetainedCareerAmmunitionCore6703(authority, loadout, out ammunition);
    }

    // The factory deep-copies and validates all loadout options. Accept the proof
    // object, never a caller digest or a boolean claiming successful validation.
    // Source/profile/category grammar and saved-item checks still run unchanged.
    internal static bool TryReadRetainedCareerAmmunition(CharacterCareerGearPurchaseSource authority,
        CharacterCreationLoadoutRules.ValidatedLoadoutEvaluation6218 validated,
        RetainedAmmunitionCategories7961 categories, out RetainedCareerAmmunition? ammunition)
    {
        ammunition = null;
        return categories.Matches(validated) && validated.Authority.RulesetId == "sr5"
            && TryReadRetainedCareerAmmunitionCore6703(authority, validated.Authority, out ammunition, categories);
    }

    private static bool TryReadRetainedCareerAmmunitionCore6703(CharacterCareerGearPurchaseSource authority,
        CharacterCreationLoadoutAuthority loadout, out RetainedCareerAmmunition? ammunition,
        RetainedAmmunitionCategories7961? projection = null)
    {
        ammunition = null;
        if (loadout.ProfileDigest != authority.ProfileDigest
            || !TryReadRetainedCareerAmmunitionSourceShape(authority, out var source, out var type)) return false;

        IReadOnlySet<string> categories = projection is null
            ? loadout.Options.Where(option => IsCareerAmmunitionCategory(option, type))
                .Select(option => option.Category).ToHashSet(StringComparer.Ordinal)
            : projection.ForType(type);
        if (categories.Count == 0) return false;
        ammunition = new(source!, categories, loadout.AuthorityDigest);
        return true;
    }

    // Reject an unsupported immutable row before resolving the full loadout.
    // This is only the same source-shape predicate used below; it grants no
    // source, profile, category, purchase or mutation authority. Admitted rows
    // still undergo the complete loadout/profile checks and canonical parser.
    internal static bool HasRetainedCareerAmmunitionSourceShape(CharacterCareerGearPurchaseSource authority)
        => TryReadRetainedCareerAmmunitionSourceShape(authority, out _, out _);

    private static bool TryReadRetainedCareerAmmunitionSourceShape(CharacterCareerGearPurchaseSource authority,
        out XElement? source, out string type)
    {
        type = string.Empty;
        if (!TryReadCareerGearSourceShape(authority, out source, out _)
            || ReadOptionalScalar(source!, "category") != "Ammunition"
            || ReadOptionalScalar(source!, "ammoforweapontype") is not { Length: > 0 } candidate
            || !SessionPlayWeaponAuthority.IsPhysicalRoundType3483(candidate)) return false;
        type = candidate;
        return true;
    }

    // Both retained inventory and new purchases use exactly this source-backed grammar.
    private static bool IsCareerAmmunitionCategory(CharacterCreationLoadoutCatalogOption option, string type)
    {
        if (option.Kind != CharacterCreationLoadoutKinds.Weapon) return false;
        var category = ParseBoundedSourceCategory(option.SourceCategoryXml);
        if (category is null || category.Value != option.Category) return false;
        if ((string?)category.Attribute("type") == type) return true;
        if (category.Value != "Exotic Ranged Weapons") return false;
        var weapon = XElement.Parse(option.SourceNodeXml);
        return ReadOptionalScalar(weapon, "weapontype") == type
            && ReadOptionalScalar(weapon, "type") == "Ranged";
    }

    internal static CharacterCareerGearPurchaseSource BindCareerAmmunitionSource(
        CharacterCareerGearPurchaseSource item, ICharacterSourceDataContext context)
    {
        // Caller-supplied witnesses cannot become live source authority.
        item = item with { Ammunition = null };
        if (!TryReadCareerGearSourceShape(item, out var source, out _)
            || string.IsNullOrEmpty(ReadOptionalScalar(source!, "ammoforweapontype"))) return item;
        if (!context.TryResolveCreationLoadoutAuthority(out var loadout)
            || !TryReadRetainedCareerAmmunition(item, loadout, out _)) return item;
        string type = ReadOptionalScalar(source!, "ammoforweapontype")!;
        var witnesses = loadout.Options.Where(option => IsCareerAmmunitionCategory(option, type))
            .GroupBy(option => option.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.OrderBy(option => option.SourceId).First()).ToArray();
        return item with { Ammunition = new(loadout.AuthorityDigest, Array.AsReadOnly(witnesses)) };
    }

    internal static string CareerAmmunitionDigest(CharacterCareerGearPurchaseSource authority)
        => CharacterCareerGearPurchaseTransaction.Hash("chummer.core.career-ammunition-categories/v1\0"
            + System.Text.Json.JsonSerializer.Serialize(authority));

    private static bool TryReadCareerAmmunitionChoices(CharacterCareerGearPurchaseSource authority, XElement source,
        out IReadOnlyList<CharacterCareerGearPurchaseAmmunitionSelection>? choices)
    {
        choices = null;
        if (authority.Ammunition is not { CategoryWitnesses.Count: > 0 and <= 256 } evidence
            || evidence.WeaponAuthorityDigest is not { Length: 71 } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal)
            || !CharacterCareerGearPurchaseTransaction.IsDigest(digest[7..])
            || ReadOptionalScalar(source, "category") != "Ammunition"
            || ReadOptionalScalar(source, "ammoforweapontype") is not { Length: > 0 } type
            || !SessionPlayWeaponAuthority.IsPhysicalRoundType3483(type)
            || evidence.CategoryWitnesses.Any(option => !CharacterCreationLoadoutRules.IsValidOption(option)
                || !IsCareerAmmunitionCategory(option, type))
            || evidence.CategoryWitnesses.Select(option => option.Category).Distinct(StringComparer.Ordinal).Count()
                != evidence.CategoryWitnesses.Count) return false;
        string binding = CareerAmmunitionDigest(authority);
        choices = Array.AsReadOnly(evidence.CategoryWitnesses.Select(option =>
            new CharacterCareerGearPurchaseAmmunitionSelection(option.Category, binding)).ToArray());
        return true;
    }

    private static XElement? ParseBoundedSourceCategory(string xml)
    {
        var category = CharacterCurrentRuleEnvironmentConsumers.ParsePurchaseXml(xml);
        return category is { Name.LocalName: "category", HasElements: false }
            && category.Name.Namespace == XNamespace.None
            && category.Value.Length > 0 && category.Value == category.Value.Trim()
            && !category.Value.Any(char.IsControl)
            && category.Attributes().All(a => a.Name.Namespace == XNamespace.None
                && a.Name.LocalName is "type" or "blackmarket" or "gunneryspec" or "translate" or "show")
                ? category : null;
    }
}
