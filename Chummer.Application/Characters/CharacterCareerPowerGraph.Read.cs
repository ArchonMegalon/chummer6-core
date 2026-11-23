using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Application.Workspaces;

namespace Chummer.Application.Characters;

internal static partial class CharacterCareerPowerGraph
{
    internal sealed record ReadOption(CharacterCareerPowerSource Source, CharacterCareerPowerIdentity Identity,
        int CurrentPaidRating, int MaximumRating, bool Discounted, bool Existing);
    internal sealed record ReadGraph(string SourceDigest, decimal? RemainingPoints,
        IReadOnlyList<CharacterCareerMagicOwnedPower> Owned, IReadOnlyList<ReadOption> Options,
        IReadOnlyList<string> Blockers)
    {
        internal IReadOnlyList<ReadPower5715> ProvenPowers { get; init; } = [];
        internal IReadOnlyList<ReadGrant5715> ProvenGrants { get; init; } = [];
    }
    internal sealed record ReadPower5715(CharacterCareerPowerIdentity Identity, string SourceXml, CharacterAdeptPowerPointCost Cost);
    internal sealed record ReadGrant5715(CharacterCareerPowerGrant Grant, string Type, int Rating, string SourceXml, string ClauseXml);

    // Called inside the sheet's existing source operation. No second resolver,
    // write admission, synthetic command or empty grant list is introduced.
    internal static ReadGraph EvaluateSheetRead5715(string xml, ICharacterSourceDataContext context,
        CharacterFinalizedLifeModuleOwnership? modules)
        => EvaluateReadCore(xml, context, modules, out _, out _);

    // Uses the same State/SourcePolicy/cost/effect compiler as Build. A read never
    // turns a saved display rating, saved bonus, or Creation budget into authority.
    internal static ReadGraph EvaluateRead(string xml, ICharacterSourceDataContext context)
        => EvaluateReadCore(xml, context, null, out _, out _);

    internal static ReadGraph EvaluateRead(WorkspaceStoredDocument saved, ICharacterSourceDataContext context)
    {
        Need(CharacterFinalizedLifeModuleOwnership.TryObserve(saved, context, out var modules),
            "power_finalized_module_ownership_unavailable");
        return EvaluateReadCore(saved.Document.Content, context, modules, out _, out _);
    }

    private sealed record CurrentQuoteOption(ReadOption Option, CharacterAdeptPowerPointInput Input, decimal OtherUsed);

    internal static bool TryQuoteCurrent(string xml, ICharacterSourceDataContext context,
        Guid sourceId, int proposedPaidRating, out CharacterAdeptPowerPointIncreaseQuote? quote)
        => TryQuoteCurrentCore(xml, context, sourceId, proposedPaidRating, null, out quote);

    internal static bool TryQuoteCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataContext context,
        Guid sourceId, int proposedPaidRating, out CharacterAdeptPowerPointIncreaseQuote? quote)
    {
        quote = null;
        return CharacterFinalizedLifeModuleOwnership.TryObserve(saved, context, out var modules)
            && TryQuoteCurrentCore(saved.Document.Content, context, sourceId, proposedPaidRating, modules, out quote);
    }

    private static bool TryQuoteCurrentCore(string xml, ICharacterSourceDataContext context,
        Guid sourceId, int proposedPaidRating, CharacterFinalizedLifeModuleOwnership? modules,
        out CharacterAdeptPowerPointIncreaseQuote? quote)
    {
        quote = null;
        try
        {
            var current = EvaluateReadCore(xml, context, modules, out var options, out var pool);
            if (current.Blockers.Count != 0 || sourceId == Guid.Empty) return false;
            // This exact-key surface has no extra/Way-choice input. Existing choices
            // keep their actual discount; a new ordinary option uses no Way discount.
            var candidates = options.Where(row => row.Option.Source.SourceId == sourceId
                && row.Option.Identity.Extra.Length == 0 && (row.Option.Existing || !row.Option.Discounted)).ToArray();
            return candidates is [var candidate]
                && CharacterAdeptPowerPointRules.TryQuoteIncrease(candidate.Input, proposedPaidRating,
                    pool, candidate.OtherUsed, out quote)
                && quote is not null && quote.HasEnoughPoints
                && quote.Proposed.PaidRating + (long)quote.Proposed.FreeLevels <= candidate.Input.MaximumTotalRating;
        }
        catch (Exception error) when (ReadUnavailable(error)) { return false; }
    }

    private static ReadGraph EvaluateReadCore(string xml, ICharacterSourceDataContext context,
        CharacterFinalizedLifeModuleOwnership? modules,
        out IReadOnlyList<CurrentQuoteOption> currentQuoteOptions, out decimal currentPool)
    {
        currentQuoteOptions = []; currentPool = 0;
        var root = Parse(xml);
        Need(root.Name == "character" && !root.HasAttributes && Bool(root, "created")
            && Bool(root, "magenabled") && Bool(root, "adept"), "power_requires_career_adept");
        Need(!Bool(root, "ignorerules", false), "power_ignore_rules_unavailable");
        Need(context.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState),
            "power_settings_unavailable");
        Need(context.TryResolveCareerPowerCatalog(out var catalog), "power_source_unavailable");
        Need(context.TryResolveCareerQualityMetatype(out var metatypeXml, out var metatypeState),
            "power_metatype_unavailable");
        var settings = Parse(settingsXml);
        Need(CharacterCareerPowerGovernance.AllowsMetadata(settings), "power_campaign_authority_unavailable");
        var evidence = new List<string> { "sr5-career-power-read/v1", settingsState, metatypeState,
            JsonSerializer.Serialize(catalog) };
        if (modules is not null) evidence.Add(modules.Digest);
        var policy = new SourcePolicy(context, root, evidence, catalog, modules);
        policy.ValidateOwnedSources();
        var effects = Container(root, "improvements", "improvement");
        var rows = Container(root, "powers", "power").Elements().ToArray();
        Need(rows.Length <= 1024 && effects.Elements().Count() <= 100_000, "power_graph_too_large");
        int magic = Magic(root, context, settings, Parse(metatypeXml), effects, evidence);
        var grants = policy.Grants(effects);
        bool magician = Bool(root, "magician"), separate = Bool(settings, "mysadeptsecondmagattribute");
        Need(CharacterAdeptPowerPointRules.TryCalculatePool(new(true, magician, separate, magic,
            magician && !separate ? Int(root, "magsplitadept") : 0, grants.Sum(g => g.PoolPoints)), out var pool),
            "power_pool_unavailable");
        var states = new List<PowerState>();
        var owned = new List<CharacterCareerMagicOwnedPower>();
        var blockers = new List<string>();
        var instances = new HashSet<Guid>();
        var choices = new HashSet<(Guid, string)>();
        foreach (var row in rows)
        {
            try
            {
                var id = new CharacterCareerPowerIdentity(GuidValue(row, "guid"), PowerSourceId(row), Value(row, "extra"));
                Need(instances.Add(id.InternalId) && choices.Add((id.SourceId, id.Extra)), "power_duplicate_identity");
                var source = catalog.SingleOrDefault(s => s.SourceId == id.SourceId);
                Need(source is not null, "power_existing_source_restricted");
                Need(Value(row, "name") == source!.Name && Value(row, "source") == source.SourceBook
                    && !Bool(row, "discountedgeas", false) && !Single(row, "enhancements", false).HasElements,
                    "power_existing_semantics_unavailable");
                var state = State(root, source, id, row, Int(row, "rating"), Bool(row, "discounted", false),
                    magic, grants, policy);
                Need(EquivalentEffects(effects.Elements().Where(e => Value(e, "sourcename", "")
                    .Equals(id.InternalId.ToString("D"), StringComparison.OrdinalIgnoreCase)),
                    CompileEffects(state.Source, id, state.Cost.TotalRating)), "power_existing_effect_mismatch");
                states.Add(state);
                owned.Add(new(id.SourceId, source.Name, id.Extra, state.Cost.TotalRating,
                    ReadEffects(state.Source, id, state.Cost.TotalRating), []));
            }
            catch (Exception e) when (ReadUnavailable(e))
            {
                string blocker = ReadBlocker(e);
                blockers.Add(blocker);
                owned.Add(UnavailableOwned(row, blocker));
            }
        }
        try
        {
            Need(!effects.Elements().Any(e => Value(e, "improvementsource", "") == "Power"
                && (!Guid.TryParse(Value(e, "sourcename", ""), out var id) || !instances.Contains(id))), "power_orphan_effect");
            RecalculateAttributeCaches(root, effects, states, Parse(metatypeXml), modules);
            Need(pool >= states.Sum(s => s.Cost.UsedPoints), "power_points_or_rating_unavailable");
        }
        catch (Exception e) when (ReadUnavailable(e))
        {
            string blocker = ReadBlocker(e);
            blockers.Add(blocker);
            for (int i = 0; i < owned.Count; i++)
                owned[i] = owned[i] with { TotalRating = null, Effects = [],
                    Blockers = Array.AsReadOnly(owned[i].Blockers.Append(blocker).Distinct().ToArray()) };
        }
        var options = new List<ReadOption>();
        var quoteOptions = new List<CurrentQuoteOption>();
        if (blockers.Count == 0)
        {
            foreach (var source in catalog)
            {
                try
                {
                    var definition = Parse(source.RawSourceXml);
                    var select = Single(definition, "bonus", false).Elements("selectattribute").ToArray();
                    Need(select.Length <= 1, "power_attribute_choice_unavailable");
                    // Enumerate actual source choices, never an invented attribute/rating range.
                    string[] extras = select.Length == 0 ? [""]
                        : select[0].Elements("attribute").Select(a => a.Value).Distinct().ToArray();
                    foreach (string extra in extras)
                    {
                        var existing = states.SingleOrDefault(s => s.Identity.SourceId == source.SourceId && s.Identity.Extra == extra);
                        bool[] discounts = existing is null ? [false, true] : [existing.Discounted];
                        foreach (bool discounted in discounts)
                        {
                            try
                            {
                                // Candidate identity is only a shape here; real new instances are issued per target below.
                                var identity = existing?.Identity ?? new CharacterCareerPowerIdentity(source.SourceId, source.SourceId, extra);
                                var state = existing ?? State(root, source, identity, null, 0, discounted, magic, grants, policy);
                                if (existing is null && states.Count(s => s.Identity.SourceId == source.SourceId)
                                    >= Int(state.Source, "limit", 1)) continue;
                                var option = new ReadOption(source, identity, state.Input.PaidRating, state.Input.MaximumTotalRating,
                                    discounted, existing is not null);
                                options.Add(option);
                                quoteOptions.Add(new(option, state.Input,
                                    states.Where(other => !ReferenceEquals(other, existing)).Sum(other => other.Cost.UsedPoints)));
                            }
                            catch (Exception e) when (ReadUnavailable(e)) { /* Unsupported new choices are not legal targets. */ }
                        }
                    }
                }
                catch (Exception e) when (ReadUnavailable(e)) { /* Owned rows were already retained above. */ }
            }
        }
        currentQuoteOptions = Array.AsReadOnly(quoteOptions.ToArray());
        currentPool = pool;
        return new(CharacterCareerPowerTransaction.Hash(JsonSerializer.Serialize(evidence)),
            blockers.Count == 0 ? pool - states.Sum(s => s.Cost.UsedPoints) : null,
            Array.AsReadOnly(owned.ToArray()), Array.AsReadOnly(options.ToArray()),
            Array.AsReadOnly(blockers.Distinct().ToArray()))
        {
            ProvenPowers = blockers.Count == 0 ? Array.AsReadOnly(states.Select(s => new ReadPower5715(
                s.Identity, s.Source.ToString(SaveOptions.DisableFormatting), s.Cost)).ToArray()) : [],
            ProvenGrants = blockers.Count == 0 ? policy.ProvenGrants.AsReadOnly() : []
        };
    }

    internal static IReadOnlyList<CharacterCareerMagicEffect> ReadEffects(
        XElement source, CharacterCareerPowerIdentity identity, int total) =>
        Array.AsReadOnly(CompileEffects(source, identity, total).Select(effect =>
        {
            string type = Value(effect, "improvementttype");
            string kind = type switch {
                "Dodge" => "dodge", "Armor" => "armor", "JudgeIntentionsDefense" => "intentiondefense",
                "DamageResistance" => "damageresistance", "Composure" => "composure", "Memory" => "memory",
                "PhysicalCMRecovery" => "physicalrecovery", "StunCMRecovery" => "stunrecovery",
                "Skill" => "skill", "Attribute" => "attribute",
                _ => throw new InvalidDataException("power_read_effect_unavailable") };
            string target = Value(effect, "improvedname");
            if (kind == "attribute") target = target switch {
                "BOD" => "Body", "AGI" => "Agility", "REA" => "Reaction", "STR" => "Strength",
                "CHA" => "Charisma", "INT" => "Intuition", "LOG" => "Logic", "WIL" => "Willpower",
                _ => throw new InvalidDataException("power_read_attribute_unavailable") };
            return new CharacterCareerMagicEffect(kind, target.Length == 0 ? Value(source, "name") : target,
                Decimal(effect, type == "Attribute" ? "aug" : "val"));
        }).ToArray());

    internal static IReadOnlyList<CharacterCareerMagicOwnedPower> UnavailableOwnedRows(XElement root, string blocker) =>
        Array.AsReadOnly(root.Elements().Where(e => e.Name.LocalName == "powers")
            .SelectMany(p => p.Elements()).Select(row => UnavailableOwned(row, blocker)).ToArray());

    private static CharacterCareerMagicOwnedPower UnavailableOwned(XElement row, string blocker)
    {
        var ids = row.Elements().Where(e => e.Name.LocalName is "sourceid" or "id").ToArray();
        Guid? id = ids.Length == 1 && Guid.TryParse(ids[0].Value, out var parsed) && parsed != Guid.Empty ? parsed : null;
        return new(id, row.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value ?? "Unsupported power",
            row.Elements().FirstOrDefault(e => e.Name.LocalName == "extra")?.Value ?? "", null, [], [blocker]);
    }

    internal static bool ReadUnavailable(Exception e) => e is XmlException or InvalidDataException or ArgumentException
        or InvalidOperationException or IOException or UnauthorizedAccessException or OverflowException;
    internal static string ReadBlocker(Exception e) => e is InvalidDataException && e.Message.StartsWith("power_", StringComparison.Ordinal)
        ? e.Message : "power_source_semantics_unavailable";
}
