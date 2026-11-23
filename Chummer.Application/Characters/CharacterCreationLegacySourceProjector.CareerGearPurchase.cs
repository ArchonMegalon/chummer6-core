using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    internal const int MaximumCareerGearCatalogRatingChoices = 256;

    // The catalog consumer creates one instance from its freshly source-resolved settings,
    // uses it only in that synchronous evaluation, then drops it. This is price data, never
    // source admission or mutation authority; every call still validates and projects its row.
    internal sealed class CareerGearPricePolicyEvaluation
    {
        private readonly CharacterCareerGearPurchaseRules.PricePolicy policy;

        internal CareerGearPricePolicyEvaluation(string settingsXml)
            => policy = CharacterCareerGearPurchaseRules.PricePolicy.Parse(settingsXml);

        internal bool TryBuildPurchasedGear(CharacterCareerGearPurchaseSource authority,
            CharacterCareerGearPurchaseRequest request, Guid gearId,
            out string payload, out decimal unitCost, out decimal total, out int availability,
            out string legality, out string error)
            => TryBuildCareerPurchasedGearCore(authority, request, null, policy, gearId,
                out payload, out unitCost, out total, out availability, out legality, out error);

        // One exact settings string is parsed by this catalog-local value owner.
        // The source tree is still validated and serialized by the canonical
        // kernel on every probe; no row, request, result or authority is cached.
        internal bool TryBuildPurchasedGearFromSource(XElement source,
            CharacterCareerGearPurchaseRequest request, Guid gearId,
            out string payload, out decimal unitCost, out decimal total, out int availability,
            out string legality, out string error)
            => TryBuildCareerGearFromSource(source, request, null, policy, gearId,
                out payload, out unitCost, out total, out availability, out legality, out error);

        internal bool TryBuildPurchasedGear(CareerGearRowEvaluation row,
            CharacterCareerGearPurchaseRequest request, Guid gearId,
            out string payload, out decimal unitCost, out decimal total, out int availability,
            out string legality, out string error)
            => row.TryBuildPurchasedGear(request, null, policy, gearId,
                out payload, out unitCost, out total, out availability, out legality, out error);
    }

    // One exact source row, scoped to one synchronous catalog evaluation. Only the constructor
    // parses source XML; no XElement, array, saved payload or admission authority is retained.
    // Invalid facts are remembered, but reported at the same request-validation stage as before.
    internal sealed class CareerGearRowEvaluation
    {
        private readonly Guid sourceId;
        private readonly bool sourceValid;
        private readonly string sourceError;
        private readonly bool ratingsValid;
        private readonly string ratingsError = "gear_rating_unsupported";
        private readonly int minimum;
        private readonly int maximum;
        private readonly string ratingExpression = "";
        private readonly string minimumExpression = "";
        private readonly string costExpression = "";
        private readonly string availExpression = "";
        private readonly GearDefinition? definition;

        internal CareerGearRowEvaluation(CharacterCareerGearPurchaseSource authority)
        {
            sourceId = authority.SourceId;
            sourceValid = TryReadCareerGearSource(authority, out var source, out sourceError);
            if (!sourceValid) return;
            ratingsValid = TryReadCareerGearRatings(source!, out minimum, out maximum,
                out ratingExpression, out minimumExpression, out ratingsError);
            if (!ratingsValid) return;
            costExpression = ReadOptionalScalar(source!, "cost") ?? "";
            availExpression = ReadOptionalScalar(source!, "avail") ?? "";
            if (source!.Element("cost") is null || source.Element("avail") is null) return;
            // Validate all fixed fields with the canonical reader. Its four varying cost/avail
            // fields are neutral seeds only: each request checks their resolved nonnegative values,
            // and the serializer below always replaces their XML with the original expressions.
            try
            {
                if (TryReadCareerGearSavedDefinition(source, 0m, 0, "", out var parsed)) definition = parsed;
            }
            catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException or OverflowException)
            { /* Report gear_saved_shape_unsupported after the per-request expression checks. */ }
        }

        internal bool TryBuildPurchasedGear(CharacterCareerGearPurchaseRequest request, string? settingsXml,
            CharacterCareerGearPurchaseRules.PricePolicy? policy, Guid gearId,
            out string payload, out decimal unitCost, out decimal total, out int availability,
            out string legality, out string error)
        {
            payload = legality = string.Empty; unitCost = total = 0m; availability = 0;
            error = "gear_source_shape_unsupported";
            if (sourceId != request.SourceId) return false;
            error = sourceError;
            if (!sourceValid) return false;
            try
            {
                if (request.ParentGearId is not null) { error = "gear_parent_capacity_unsupported"; return false; }
                if (request.StackTargetId is not null) { error = "gear_stack_purchase_unsupported"; return false; }
                // Eligibility still needs an independently compiled quality/category mapping.
                if (request.BlackMarketDiscount) { error = "gear_black_market_eligibility_unsupported"; return false; }
                error = ratingsError;
                if (!ratingsValid) return false;
                if (request.Rating < minimum || request.Rating > maximum) { error = "gear_rating_out_of_range"; return false; }
                if (!TryEvaluateCareerGearExpressions(costExpression, availExpression, request.Rating,
                    out decimal cost, out availability, out legality, out error)) return false;
                error = "gear_saved_shape_unsupported";
                // The canonical reader requires a nonnegative decimal cost and a nonnegative
                // Int32 availability. Invariant rendering of these values always roundtrips.
                if (definition is null || cost < 0m || availability < 0) return false;
                return TryProjectCareerGearValue(definition, request, settingsXml, policy, gearId,
                    costExpression, availExpression, minimumExpression, ratingExpression, maximum, legality,
                    out payload, out unitCost, out total, out error);
            }
            catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException or OverflowException)
            { return false; }
        }
    }

    internal static CharacterCareerGearPurchaseCatalogRow DescribeCareerGearPurchaseSource(
        CharacterCareerGearPurchaseSource authority, string settingsXml, CharacterCareerGearPurchaseBinding binding)
    {
        var options = new CharacterCareerGearPurchaseCatalogOptions(
            new(CharacterCareerGearPurchaseRules.MinimumMarkupPercent, CharacterCareerGearPurchaseRules.MaximumMarkupPercent,
                CharacterCareerGearPurchaseRules.MarkupIncrement, 0m),
            new(false, "gear_black_market_eligibility_unsupported"), new(false, "gear_parent_capacity_unsupported"),
            new(false, "gear_stack_purchase_unsupported"));
        CharacterCareerGearPurchaseCatalogRow Blocked(string error, CharacterCareerGearPurchaseCatalogRatings? ratings = null)
            => new(authority.SourceId, authority.Name, authority.SourceBook, false, ratings, null, options, [error], null);
        if (!TryReadCareerPurchaseSource(authority, out var source, out var choices, out string error)) return Blocked(error);
        if (!TryReadCareerGearRatings(source!, out int minimum, out int maximum, out _, out _, out error)) return Blocked(error);
        // A huge custom range is unknown to this bounded query, never silently truncated or declared eligible.
        if ((long)maximum - minimum + 1 > MaximumCareerGearCatalogRatingChoices)
            return Blocked("gear_catalog_rating_range_limit", new(minimum, maximum, []));
        var values = new List<CharacterCareerGearPurchaseCatalogRating>();
        CharacterCareerGearPurchaseCatalogQuantity? quantity = null;
        for (long value = minimum; value <= maximum; value++)
        {
            int rating = (int)value;
            if (!TryReadCareerGearDefinition(source!, rating, out var definition, out _, out _, out _, out string legality, out error))
            { values.Add(new(rating, [error])); continue; }
            if (!CharacterCareerGearPurchaseRules.TryReadPricePolicy(legality, settingsXml, out _, out int? decimals))
            { values.Add(new(rating, ["gear_price_policy_unresolved"])); continue; }
            if (!CharacterGearQuantityRules.TryResolvePrecision(definition.Name, definition.Category, decimals,
                out int places, out decimal increment))
            { values.Add(new(rating, ["gear_quantity_invalid"])); continue; }
            var unit = definition.Name.StartsWith("Nuyen", StringComparison.Ordinal) ? CharacterCareerGearPurchaseQuantityUnit.Nuyen
                : definition.Category == "Currency" ? CharacterCareerGearPurchaseQuantityUnit.Currency
                : choices is not null ? CharacterCareerGearPurchaseQuantityUnit.Round : CharacterCareerGearPurchaseQuantityUnit.Item;
            var candidate = new CharacterCareerGearPurchaseCatalogQuantity(unit, places, increment,
                CharacterGearQuantityRules.MaximumQuantity, increment, 1m, definition.PackageQuantity);
            // Use the actual serializer/pricing/quantity authority at the minimum legal amount.
            // These deterministic identifiers are used only to exercise serialization; nothing is dispatched or saved.
            var request = new CharacterCareerGearPurchaseRequest(binding.WorkspaceId, authority.SourceId, authority.SourceId,
                rating, increment, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified)) { Ammunition = choices?.First() };
            if (!TryBuildCareerPurchasedGear(authority, request, settingsXml, authority.SourceId,
                out _, out _, out _, out _, out _, out error))
            { values.Add(new(rating, [error])); continue; }
            quantity = candidate;
            values.Add(new(rating, []));
        }
        bool supported = values.Any(value => value.Blockers.Count == 0);
        return new(authority.SourceId, authority.Name, authority.SourceBook, supported,
            new(minimum, maximum, Array.AsReadOnly(values.ToArray())), quantity, options,
            supported ? [] : Array.AsReadOnly(values.SelectMany(value => value.Blockers).Distinct(StringComparer.Ordinal).ToArray()),
            supported ? new(authority.SourceId, binding) : null) { AmmunitionChoices = supported ? choices : null };
    }

    /// <summary>Career adapter to the canonical Gear.Create/Save serializer; no Creation state is consulted or mutated.</summary>
    internal static bool TryBuildCareerPurchasedGear(CharacterCareerGearPurchaseSource authority,
        CharacterCareerGearPurchaseRequest request, string settingsXml, Guid gearId,
        out string payload, out decimal unitCost, out decimal total, out int availability,
        out string legality, out string error)
        => TryBuildCareerPurchasedGearCore(authority, request, settingsXml, null, gearId,
            out payload, out unitCost, out total, out availability, out legality, out error);

    private static bool TryBuildCareerPurchasedGearCore(CharacterCareerGearPurchaseSource authority,
        CharacterCareerGearPurchaseRequest request, string? settingsXml,
        CharacterCareerGearPurchaseRules.PricePolicy? policy, Guid gearId,
        out string payload, out decimal unitCost, out decimal total, out int availability,
        out string legality, out string error)
    {
        payload = legality = string.Empty; unitCost = total = 0m; availability = 0;
        error = "gear_source_shape_unsupported";
        if (authority.SourceId != request.SourceId
            || !TryReadCareerPurchaseSource(authority, out var source, out var choices, out error)) return false;
        if (choices is null ? request.Ammunition is not null : request.Ammunition is null || !choices.Contains(request.Ammunition))
        { error = "gear_ammunition_category_required_or_stale"; return false; }
        if (!TryBuildCareerGearFromSource(source!, request, settingsXml, policy, gearId,
            out payload, out unitCost, out total, out availability, out legality, out error)) return false;
        if (request.Ammunition is { } choice)
        {
            var gear = XElement.Parse(payload);
            gear.Element("extra")!.Value = choice.WeaponCategory;
            payload = gear.ToString(SaveOptions.DisableFormatting);
        }
        return true;
    }

    private static bool TryBuildCareerGearFromSource(XElement source,
        CharacterCareerGearPurchaseRequest request, string? settingsXml,
        CharacterCareerGearPurchaseRules.PricePolicy? policy, Guid gearId,
        out string payload, out decimal unitCost, out decimal total, out int availability,
        out string legality, out string error)
    {
        payload = legality = string.Empty; unitCost = total = 0m; availability = 0;
        error = "gear_source_shape_unsupported";
        try
        {
            if (request.ParentGearId is not null) { error = "gear_parent_capacity_unsupported"; return false; }
            if (request.StackTargetId is not null) { error = "gear_stack_purchase_unsupported"; return false; }
            // Black-market eligibility needs an independently compiled quality/category mapping.
            // Do not trust a saved flag or silently turn a requested discount off.
            if (request.BlackMarketDiscount) { error = "gear_black_market_eligibility_unsupported"; return false; }
            if (!TryReadCareerGearRatings(source!, out int minimum, out int maximum,
                out string ratingExpression, out string minimumExpression, out error)) return false;
            if (request.Rating < minimum || request.Rating > maximum) { error = "gear_rating_out_of_range"; return false; }
            if (!TryReadCareerGearDefinition(source!, request.Rating, out var definition,
                out string costExpression, out string availExpression, out availability, out legality, out error)) return false;
            return TryProjectCareerGearValue(definition, request, settingsXml, policy, gearId,
                costExpression, availExpression, minimumExpression, ratingExpression, maximum, legality,
                out payload, out unitCost, out total, out error);
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return false; }
    }

    // Both raw-input calls and repeated probes use the same pricing, quantity and XML writer.
    // BuildSavedGear creates a fresh tree; all per-request fields are overwritten before exposure.
    private static bool TryProjectCareerGearValue(GearDefinition definition,
        CharacterCareerGearPurchaseRequest request, string? settingsXml,
        CharacterCareerGearPurchaseRules.PricePolicy? policy, Guid gearId,
        string costExpression, string availExpression, string minimumExpression,
        string ratingExpression, int maximum, string legality,
        out string payload, out decimal unitCost, out decimal total, out string error)
    {
        payload = string.Empty; unitCost = total = 0m; error = string.Empty;
        var costState = new CharacterGearCostSnapshot(request.Rating, request.Quantity, costExpression,
            definition.PackageQuantity, false, definition.ChildCostMultiplier, []);
        error = "gear_price_policy_unresolved";
        int? decimals;
        bool priced = policy is null
            ? CharacterCareerGearPurchaseRules.TryPrice(costState, request.Quantity, request.MarkupPercent, legality, settingsXml!,
                out unitCost, out total, out decimals)
            : policy.TryPrice(costState, request.Quantity, request.MarkupPercent, legality,
                out unitCost, out total, out decimals);
        if (!priced) return false;
        error = "gear_quantity_invalid";
        if (!CharacterGearQuantityRules.TryResolvePrecision(definition.Name, definition.Category, decimals, out _, out decimal increment)
            || !CharacterGearQuantityRules.IsValidAmount(request.Quantity, increment)) return false;
        var gear = BuildSavedGear(definition, gearId.ToString("D"), 1);
        gear.Element("rating")!.Value = request.Rating.ToString(CultureInfo.InvariantCulture);
        gear.Element("minrating")!.Value = minimumExpression;
        gear.Element("maxrating")!.Value = maximum == 0 ? "" : ratingExpression;
        gear.Element("qty")!.Value = request.Quantity.ToString(CultureInfo.InvariantCulture);
        gear.Element("cost")!.Value = costExpression;
        gear.Element("avail")!.Value = availExpression;
        payload = gear.ToString(SaveOptions.DisableFormatting);
        error = string.Empty;
        return true;
    }

    private static bool TryReadCareerPurchaseSource(CharacterCareerGearPurchaseSource authority,
        out XElement? source, out IReadOnlyList<CharacterCareerGearPurchaseAmmunitionSelection>? choices, out string error)
    {
        choices = null;
        if (!TryReadCareerGearSourceShape(authority, out source, out error)) return false;
        if (string.IsNullOrWhiteSpace(ReadOptionalScalar(source!, "ammoforweapontype")))
        {
            if (authority.Ammunition is null) return true;
            error = "gear_ammunition_category_source_mismatch"; return false;
        }
        if (TryReadCareerAmmunitionChoices(authority, source!, out choices)) return true;
        error = "gear_ammunition_weapon_category_selection_unsupported"; return false;
    }

    private static bool TryReadCareerGearSource(CharacterCareerGearPurchaseSource authority,
        out XElement? source, out string error)
    {
        if (!TryReadCareerGearSourceShape(authority, out source, out error)) return false;
        if (!string.IsNullOrWhiteSpace(ReadOptionalScalar(source!, "ammoforweapontype")))
        { error = "gear_ammunition_weapon_category_selection_unsupported"; return false; }
        return true;
    }

    private static bool TryReadCareerGearSourceShape(CharacterCareerGearPurchaseSource authority,
        out XElement? source, out string error)
    {
        source = null;
        error = "gear_source_shape_unsupported";
        if (authority.SourceXml is not { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(authority.SourceXml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = CharacterCreationGearRules.MaximumSourceNodeLength });
            var original = XElement.Load(reader);
            if (original.Name != "gear" || original.HasAttributes || original.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) return false;
            var unsupported = original.Elements().FirstOrDefault(e => e.Name.NamespaceName.Length != 0 || (!s_GearSourceChildren.Contains(e.Name.LocalName) && e.Name != "addoncategory"));
            if (unsupported is not null) { error = "gear_source_field_unsupported:" + unsupported.Name.LocalName; return false; }
            // Like the existing granted-gear adapter, retain addon categories in source evidence;
            // they constrain future attachment UI and are not saved instance fields.
            if (original.Elements("addoncategory").Any(e => e.HasAttributes || e.HasElements || string.IsNullOrWhiteSpace(e.Value))) return false;
            original.Elements("addoncategory").Remove();
            source = ParseBoundedSource(original.ToString(SaveOptions.DisableFormatting), "gear", s_GearSourceChildren);
            if (source is null || !TryReadGuid(source, "id", out var id) || id != authority.SourceId
                || ReadOptionalScalar(source, "name") != authority.Name || ReadOptionalScalar(source, "source") != authority.SourceBook) return false;
            if (authority.Name == "Custom Item") { error = "gear_custom_name_selection_unsupported"; return false; }
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException or OverflowException)
        { return false; }
    }

    private static bool TryReadCareerGearRatings(XElement source, out int minimum, out int maximum,
        out string ratingExpression, out string minimumExpression, out string error)
    {
        minimum = maximum = 0;
        ratingExpression = ReadOptionalScalar(source, "rating") ?? "";
        minimumExpression = ReadOptionalScalar(source, "minrating") ?? "";
        error = "gear_rating_unsupported";
        if (!int.TryParse(ratingExpression, NumberStyles.None, CultureInfo.InvariantCulture, out maximum) || maximum < 0) return false;
        minimum = maximum == 0 ? 0 : 1;
        if (minimumExpression.Length != 0)
        {
            if (!int.TryParse(minimumExpression, NumberStyles.None, CultureInfo.InvariantCulture, out minimum) || minimum < 0) return false;
            if (minimum == 0 && maximum > 0) minimum = 1; // Gear.MinRatingValue.
        }
        if (minimum > maximum) { error = "gear_rating_out_of_range"; return false; }
        error = string.Empty;
        return true;
    }

    private static bool TryReadCareerGearDefinition(XElement source, int rating,
        out GearDefinition definition, out string costExpression, out string availExpression,
        out int availability, out string legality, out string error)
    {
        definition = null!;
        availability = 0;
        legality = availExpression = string.Empty;
        costExpression = ReadOptionalScalar(source, "cost") ?? "";
        availExpression = ReadOptionalScalar(source, "avail") ?? "";
        if (!TryEvaluateCareerGearExpressions(costExpression, availExpression, rating,
            out decimal cost, out availability, out legality, out error)) return false;
        error = "gear_saved_shape_unsupported";
        if (!TryReadCareerGearSavedDefinition(source, cost, availability, legality, out definition)) return false;
        error = string.Empty;
        return true;
    }

    private static bool TryEvaluateCareerGearExpressions(string costExpression, string availExpression, int rating,
        out decimal cost, out int availability, out string legality, out string error)
    {
        cost = 0m; availability = 0; legality = string.Empty;
        error = "gear_cost_expression_unsupported";
        if (string.IsNullOrWhiteSpace(costExpression)
            || !CharacterGearQuantityRules.TryEvaluateCostExpression(costExpression, rating, out cost)) return false;
        error = "gear_availability_expression_unsupported";
        if (string.IsNullOrWhiteSpace(availExpression)) return false;
        string numeric = availExpression;
        if (numeric.EndsWith('R') || numeric.EndsWith('F')) { legality = numeric[^1..]; numeric = numeric[..^1]; }
        if (string.IsNullOrWhiteSpace(numeric) || !CharacterGearQuantityRules.TryEvaluateCostExpression(numeric, rating, out decimal resolved)
            || resolved != decimal.Truncate(resolved) || resolved > int.MaxValue) return false;
        availability = (int)resolved;
        error = string.Empty;
        return true;
    }

    private static bool TryReadCareerGearSavedDefinition(XElement source, decimal cost, int availability,
        string legality, out GearDefinition definition)
    {
        source = new XElement(source);
        // Normalize only the three already evaluated fields for the existing definition reader.
        // Every other field still passes its canonical allowlist, parser and serializer.
        source.Element("rating")!.Value = "0";
        source.Elements("minrating").Remove();
        source.Element("cost")!.Value = cost.ToString(CultureInfo.InvariantCulture);
        source.Element("avail")!.Value = availability.ToString(CultureInfo.InvariantCulture) + legality;
        return TryReadGearDefinition(source, out definition);
    }
}
