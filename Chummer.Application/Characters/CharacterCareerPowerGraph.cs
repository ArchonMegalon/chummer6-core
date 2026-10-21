using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded SR5 compiler, using enabled source rows rather than saved prices or
/// saved bonus XML. Unsupported source/effect semantics reject the operation.
/// Existing nodes are never dropped to make an otherwise invalid runner fit.
/// </summary>
internal static partial class CharacterCareerPowerGraph
{
    internal sealed record Projection(string Xml, string AuthorityDigest,
        CharacterAdeptPowerPointIncreaseQuote Quote, IReadOnlyList<CharacterCareerPowerGrant> Grants);

    internal static Projection Build(string xml, ICharacterSourceDataContext context, CharacterCareerPowerCommand command)
    {
        var root = Parse(xml);
        Need(root.Name == "character" && !root.HasAttributes && Bool(root, "created")
            && Bool(root, "adept") && Bool(root, "magenabled"), "power_requires_career_adept");
        Need(command.Identity is not null && command.TransactionId != Guid.Empty
            && command.Identity.InternalId != Guid.Empty && command.Identity.SourceId != Guid.Empty
            && command.Identity.Extra is { Length: <= 1024 }
            && command.Identity.Extra == command.Identity.Extra.Trim()
            && command.PaidRating is > 0 and <= 1000, "power_command_invalid");
        Need(!Bool(root, "ignorerules", false), "power_ignore_rules_unavailable");
        Need(context.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState), "power_settings_unavailable");
        Need(context.TryResolveCareerPowerCatalog(out var catalog), "power_source_unavailable");
        // Kept separate from Creation prerequisite/Confirm authority.
        var settings = Parse(settingsXml);
        Need(CharacterCareerPowerGovernance.AllowsMetadata(settings), "power_campaign_authority_unavailable");
        var evidence = new List<string> { "sr5-career-power-owner-managed/v1", settingsState, JsonSerializer.Serialize(catalog) };
        Need(context.TryResolveCareerQualityMetatype(out var metatypeXml, out var metatypeState), "power_metatype_unavailable");
        evidence.Add(metatypeState);
        var policies = new SourcePolicy(context, root, evidence, catalog);
        policies.ValidateOwnedSources();
        var effects = Container(root, "improvements", "improvement");
        var powers = Container(root, "powers", "power");
        Need(powers.Elements().Count() <= 1024 && effects.Elements().Count() <= 100_000, "power_graph_too_large");
        int magic = Magic(root, context, settings, Parse(metatypeXml), effects, evidence);
        var grants = policies.Grants(effects);
        bool magician = Bool(root, "magician");
        bool separate = Bool(settings, "mysadeptsecondmagattribute");
        int purchased = magician && !separate ? Int(root, "magsplitadept") : 0;
        Need(CharacterAdeptPowerPointRules.TryCalculatePool(new(true, magician, separate, magic, purchased,
            grants.Sum(g => g.PoolPoints)), out decimal pool), "power_pool_unavailable");
        var rows = powers.Elements().ToArray();
        var instanceIds = new HashSet<Guid>();
        var choices = new HashSet<(Guid, string)>();
        var states = new List<PowerState>();
        PowerState? target = null;
        foreach (var row in rows)
        {
            var id = new CharacterCareerPowerIdentity(GuidValue(row, "guid"), PowerSourceId(row), Value(row, "extra"));
            Need(instanceIds.Add(id.InternalId) && choices.Add((id.SourceId, id.Extra)), "power_duplicate_identity");
            var source = catalog.SingleOrDefault(s => s.SourceId == id.SourceId);
            Need(source is not null, "power_existing_source_restricted");
            Need(Value(row, "name") == source!.Name && Value(row, "source") == source.SourceBook
                && !Bool(row, "discountedgeas", false) && !Single(row, "enhancements", false).HasElements,
                "power_existing_semantics_unavailable");
            var state = State(root, source, id, row, Int(row, "rating"), Bool(row, "discounted", false),
                magic, grants, policies);
            states.Add(state);
            if (id.InternalId == command.Identity.InternalId)
            {
                Need(id == command.Identity && state.Discounted == command.ApplyWayDiscount, "power_identity_or_discount_changed");
                target = state;
            }
        }
        if (target is null)
        {
            Need(!choices.Contains((command.Identity.SourceId, command.Identity.Extra))
                && !root.Descendants("guid").Any(e => Guid.TryParse(e.Value, out var id) && id == command.Identity.InternalId)
                && !effects.Elements().Any(e => Value(e, "sourcename", "") == command.Identity.InternalId.ToString("D")),
                "power_add_identity_collision");
            var source = catalog.SingleOrDefault(s => s.SourceId == command.Identity.SourceId);
            Need(source is not null, "power_source_forbidden");
            target = State(root, source!, command.Identity, null, 0, command.ApplyWayDiscount, magic, grants, policies);
            // Source limit counts instances, not paid ratings.
            int limit = Int(target.Source, "limit", 1);
            Need(limit > 0 && states.Count(s => s.Identity.SourceId == source!.SourceId) < limit, "power_instance_limit");
            states.Add(target);
        }
        // External grant rows must bind one real instance/choice, including a
        // newly selected power. A free grant is not a second paid instance.
        Need(grants.Where(g => g.PowerName.Length > 0).All(g => states.Count(s =>
            Value(s.Source, "name") == g.PowerName && s.Identity.Extra == g.Extra) == 1), "power_grant_target_unavailable");
        decimal other = states.Where(s => s != target).Sum(s => s.Cost.UsedPoints);
        Need(CharacterAdeptPowerPointRules.TryQuoteIncrease(target.Input, command.PaidRating, pool, other, out var quote)
            && quote is not null && quote.HasEnoughPoints, "power_points_or_rating_unavailable");
        Need(quote!.Proposed.PaidRating + (long)quote.Proposed.FreeLevels <= target.Input.MaximumTotalRating,
            "power_paid_levels_discarded");
        // Check every old power effect against its enabled source at its effective
        // rating. No saved bonus XML or orphan effect can become executable input.
        var ownedIds = rows.Select(r => GuidValue(r, "guid").ToString("D")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(!effects.Elements().Any(e => Value(e, "improvementsource", "") == "Power"
            && !ownedIds.Contains(Value(e, "sourcename", ""))), "power_orphan_effect");
        foreach (var state in states)
        {
            var oldEffects = CompileEffects(state.Source, state.Identity, state.Cost.TotalRating);
            int effective = state == target ? quote!.Proposed.TotalRating : state.Cost.TotalRating;
            var newEffects = CompileEffects(state.Source, state.Identity, effective);
            if (state.Saved is not null)
            {
                var actual = effects.Elements().Where(e => Value(e, "sourcename", "")
                    .Equals(state.Identity.InternalId.ToString("D"), StringComparison.OrdinalIgnoreCase)).ToArray();
                Need(EquivalentEffects(actual, oldEffects), "power_existing_effect_mismatch");
                if (effective == state.Cost.TotalRating) continue;
                for (int i = 0; i < oldEffects.Length; i++)
                {
                    var old = actual.First(e => EffectKey(e) == EffectKey(oldEffects[i]));
                    foreach (var name in new[] { "notes", "notesColor", "order" })
                        if (old.Element(name) is { } value) newEffects[i].SetElementValue(name, value.Value);
                }
                foreach (var effect in actual) effect.Remove();
            }
            effects.Add(newEffects);
        }
        if (target.Saved is null)
            powers.Add(SavedPower(target, command.PaidRating));
        else Single(target.Saved, "rating").Value = command.PaidRating.ToString(CultureInfo.InvariantCulture);
        RecalculateAttributeCaches(root, effects, states, Parse(metatypeXml));
        return new(root.ToString(SaveOptions.DisableFormatting),
            CharacterCareerPowerTransaction.Hash(JsonSerializer.Serialize(evidence)), quote!, grants);
    }

    private sealed record PowerState(CharacterCareerPowerIdentity Identity, XElement Source, XElement? Saved,
        CharacterAdeptPowerPointInput Input, CharacterAdeptPowerPointCost Cost, bool Discounted);

    private static PowerState State(XElement root, CharacterCareerPowerSource source,
        CharacterCareerPowerIdentity identity, XElement? saved, int paid, bool discounted, int magic,
        IReadOnlyList<CharacterCareerPowerGrant> grants, SourcePolicy policy)
    {
        var definition = Parse(source.RawSourceXml);
        Need(GuidValue(definition, "id") == source.SourceId && Value(definition, "name") == source.Name,
            "power_source_identity_invalid");
        string[] supported = ["id", "name", "points", "levels", "limit", "source", "page", "adeptway",
            "adeptwayrequires", "bonus", "doublecost", "extrapointcost", "maxlevels", "maxlevel", "action",
            "notes", "altnotes", "notesColor", "required", "forbidden", "translate", "altpage"];
        Need(definition.Elements().All(e => supported.Contains(e.Name.LocalName) && e.Name.NamespaceName == "")
            && definition.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
            && !(definition.Element("maxlevels") is not null && definition.Element("maxlevel") is not null)
            && !Bool(definition, "doublecost", false), "power_source_semantics_unavailable");
        Need(policy.Requirements(definition), "power_prerequisite_unavailable");
        bool levels = Bool(definition, "levels");
        int max = MaximumLevel(definition);
        max = levels ? Math.Min(max <= 0 ? magic : max, magic) : 1;
        decimal discount = 0;
        if (discounted)
        {
            discount = Decimal(definition, "adeptway", 0);
            Need(discount > 0 && policy.WayAllowed(definition), "power_way_unavailable");
        }
        var matching = grants.Where(g => g.PowerName == source.Name && g.Extra == identity.Extra).ToArray();
        var input = new CharacterAdeptPowerPointInput(paid, levels, max, magic,
            Decimal(definition, "points"), Decimal(definition, "extrapointcost", 0), discount,
            matching.Sum(g => g.FreeLevels), matching.Sum(g => g.FreePointQuarterUnits));
        if (saved is not null)
            Need(Decimal(saved, "pointsperlevel") == input.PointsPerLevel
                && Decimal(saved, "extrapointcost") == input.ExtraPointCost && Bool(saved, "levels") == levels
                && MaximumLevel(saved) == MaximumLevel(definition)
                && Decimal(saved, "adeptway", 0) == Decimal(definition, "adeptway", 0)
                && XNode.DeepEquals(Single(saved, "bonus", false), Single(definition, "bonus", false))
                && XNode.DeepEquals(Single(saved, "adeptwayrequires", false), Single(definition, "adeptwayrequires", false)),
                "power_saved_definition_drift");
        Need(CharacterAdeptPowerPointRules.TryCalculateCost(input, out var cost), "power_cost_unavailable");
        Need(cost!.PaidRating == 0 || cost.PaidRating + (long)cost.FreeLevels <= input.MaximumTotalRating,
            "power_saved_paid_levels_discarded");
        // This compilation also validates prompt/choice identity when rating is zero.
        _ = CompileEffects(definition, identity, cost!.TotalRating);
        return new(identity, definition, saved, input, cost, discounted);
    }

    private static int MaximumLevel(XElement node)
    {
        Need(!(node.Element("maxlevels") is not null && node.Element("maxlevel") is not null),
            "power_maximum_alias_ambiguous");
        return Int(node, "maxlevels", Int(node, "maxlevel", 0));
    }

    private static XElement SavedPower(PowerState state, int paid)
    {
        var s = state.Source;
        return new("power",
            new XElement("sourceid", state.Identity.SourceId.ToString("D")),
            new XElement("guid", state.Identity.InternalId.ToString("D")),
            new XElement("name", Value(s, "name")), new XElement("extra", state.Identity.Extra),
            new XElement("pointsperlevel", Value(s, "points")), new XElement("adeptway", Decimal(s, "adeptway", 0)),
            new XElement("action", Value(s, "action", "")), new XElement("rating", paid),
            new XElement("extrapointcost", Decimal(s, "extrapointcost", 0)),
            new XElement("levels", Bool(s, "levels")), new XElement("maxlevels", Int(s, "maxlevels", Int(s, "maxlevel", 0))),
            new XElement("discounted", state.Discounted), new XElement("discountedgeas", false),
            new XElement("bonussource", ""), new XElement("freepoints", 0),
            new XElement("source", Value(s, "source")), new XElement("page", Value(s, "page")),
            new XElement("bonus", Single(s, "bonus", false).Nodes()),
            new XElement("adeptwayrequires", Single(s, "adeptwayrequires", false).Nodes()),
            new XElement("enhancements"), new XElement("notes", Value(s, "notes", "")),
            new XElement("notesColor", Value(s, "notesColor", "#000000")));
    }

    internal static XElement Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    internal static void Need([DoesNotReturnIf(false)] bool value, string error)
    { if (!value) throw new InvalidDataException(error); }
    internal static XElement Single(XElement parent, string name, bool required = true)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Need(rows.Length <= 1 && (!required || rows.Length == 1), "power_ambiguous_" + name);
        var row = rows.SingleOrDefault() ?? new XElement(name);
        Need(row.Name == name && !row.HasAttributes, "power_invalid_" + name);
        return row;
    }
    internal static string Value(XElement parent, string name, string? fallback = null)
    {
        var node = Single(parent, name, fallback is null);
        Need(!node.HasElements && node.Value == node.Value.Trim(), "power_scalar_" + name);
        return parent.Element(name) is null ? fallback! : node.Value;
    }
    internal static int Int(XElement p, string n, int? fallback = null)
    {
        Need(int.TryParse(Value(p, n, fallback?.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) && v is >= 0 and <= 1000,
            "power_integer_" + n);
        return v;
    }
    internal static decimal Decimal(XElement p, string n, decimal? fallback = null)
    {
        var text = Value(p, n, fallback?.ToString(CultureInfo.InvariantCulture));
        // Bounded scale prevents decimal.TryParse's silent rounding.
        Need(text.Length <= 20 && decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out _), "power_decimal_" + n);
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }
    internal static bool Bool(XElement p, string n, bool? fallback = null)
    {
        var value = Value(p, n, fallback?.ToString());
        Need(value is "True" or "False" or "true" or "false" or "1" or "0", "power_boolean_" + n);
        return value is "True" or "true" or "1";
    }
    internal static Guid GuidValue(XElement p, string n)
    {
        Need(Guid.TryParseExact(Value(p, n), "D", out var id) && id != Guid.Empty, "power_guid_" + n);
        return id;
    }
    internal static Guid PowerSourceId(XElement p)
    {
        Need(!(p.Element("sourceid") is not null && p.Element("id") is not null), "power_source_alias_ambiguous");
        return GuidValue(p, p.Element("sourceid") is not null ? "sourceid" : "id");
    }
    private static XElement Container(XElement p, string name, string item)
    {
        var node = Single(p, name);
        Need(node.Elements().All(e => e.Name == item) && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
            "power_container_" + name);
        return node;
    }
}
