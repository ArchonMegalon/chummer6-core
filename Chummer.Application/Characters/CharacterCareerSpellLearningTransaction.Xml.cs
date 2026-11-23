using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerSpellLearningTransaction
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly HashSet<string> SpellFields = new(StringComparer.Ordinal)
        { "id", "name", "page", "source", "category", "damage", "descriptor", "duration", "dv", "range", "type",
          "useskill", "notes", "altnotes", "notesColor", "translate", "altpage" };
    private static readonly HashSet<string> TraditionFields = new(StringComparer.Ordinal)
        { "id", "name", "drain", "source", "page", "spirits", "spiritform", "notes", "altnotes", "notesColor", "translate", "altpage" };
    private static readonly HashSet<string> EffectFields = new(StringComparer.Ordinal)
        { "unique", "target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating", "exclude",
          "condition", "improvementttype", "improvementsource", "custom", "customname", "customid", "customgroup",
          "addtorating", "enabled", "order", "notes", "notesColor" };

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerSpellLearningRequest request,
        ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerSpellLearningSnapshot? snapshot, out string error)
        => TryReadCurrent5703(owner, saved, request, sources, runtimeDigest, null, out snapshot, out error);

    internal static bool TryReadCurrent5703(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerSpellLearningRequest request,
        ICharacterSourceDataResolver sources, string runtimeDigest,
        CharacterCurrentRuleEnvironmentContext? current, out CharacterCareerSpellLearningSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "spell_learning_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            var learning = context is null ? null : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(
                saved, current, context, false);
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, out snapshot, out error, learning);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryReadCore(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerSpellLearningRequest request,
        ICharacterSourceDataContext context, string runtimeDigest,
        out CharacterCareerSpellLearningSnapshot? snapshot, out string error,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? learning = null)
    {
        snapshot = null;
        error = "spell_learning_state_invalid";
        try
        {
            Need(IsValidOwner(owner) && IsValidRequest(request) && saved.Id == request.WorkspaceId
                && saved.ContentRevision is > 0 and < long.MaxValue && saved.SavedRevision == saved.ContentRevision
                && IsDigest(runtimeDigest), "spell_learning_workspace_invalid");
            Need(learning is null || learning.Matches(saved, context), "spell_learning_current_changed");
            Need(CharacterCareerPowerGovernance.Allows(saved), "spell_learning_requires_local_saved_sr5_career");
            if (request.CatalogBinding is { } selectedCatalog)
                Need(TryReadCatalogCore(owner, saved, context, runtimeDigest, out var catalog, out _, learning)
                    && catalog!.Binding == selectedCatalog, "spell_learning_catalog_changed");
            Need(!request.Limited && !request.Extended && !request.Alchemical && !request.Free,
                "spell_learning_variant_unsupported");
            var root = learning is null ? Parse(saved.Document.Content, "character") : new XElement(learning.Graph);
            Need(Flag(root, "created") && !Flag(root, "ignorerules", false), "spell_learning_requires_rules_career");
            Need(context.TryResolveCareerSettingsProfile(out var profile, out var settingsAuthority)
                && !string.IsNullOrWhiteSpace(settingsAuthority), "spell_learning_profile_unavailable");
            var settings = Parse(profile, "settings");
            Need(CharacterCareerSettingsProfileBinding.MatchesSelectedProfile(Scalar(root, "settings"), settings, context, "spell_learning"),
                "spell_learning_profile_binding_invalid");
            var magic = learning is null ? CharacterCareerMagicEvaluator.Evaluate(root, settings, sourceContext: context)
                : learning.Magic ?? throw new InvalidDataException("spell_learning_imported_magic_unavailable");
            Need(magic.IsSupported, magic.Blockers.FirstOrDefault() ?? "spell_learning_magic_unavailable");
            Need(magic.IsMagician && magic.Magic > 0, "spell_learning_requires_magician_magic");
            var ownedTradition = Box(root, "tradition");
            Need(Guid.TryParse(Scalar(ownedTradition, "sourceid"), out var traditionId) && traditionId != Guid.Empty
                && Scalar(ownedTradition, "traditiontype") == "MAG" && Scalar(ownedTradition, "extra", "").Length == 0,
                "spell_learning_tradition_unavailable");
            Need(context is ICharacterCareerSpellLearningSourceContext, "spell_learning_source_capability_unavailable");
            Need(((ICharacterCareerSpellLearningSourceContext)context).TryResolveCareerSpellLearningSource(
                request.SourceId, traditionId, out var source) && source is not null
                && !string.IsNullOrWhiteSpace(source.AuthorityState), "spell_learning_source_unavailable");
            var spell = Parse(source!.SpellXml, "spell");
            var tradition = Parse(source.TraditionXml, "tradition");
            ValidateSource(request, spell, tradition, traditionId);
            Need(Scalar(ownedTradition, "name") == Scalar(tradition, "name")
                && Scalar(ownedTradition, "source") == Scalar(tradition, "source")
                && Scalar(ownedTradition, "drain") == Scalar(tradition, "drain")
                && !Box(ownedTradition, "bonus").HasElements && Box(ownedTradition, "bonus").Value.Trim().Length == 0,
                "spell_learning_tradition_binding_invalid");
            Need(CurrentSourceVisible(settings, spell),
                "spell_learning_extended_source_filtered_by_profile");
            int baseCost = Integer(Box(settings, "karmacost"), "karmaspell");
            Need(baseCost >= 0, "spell_learning_profile_cost_invalid");
            var effects = ReadEffects(root, spell, learning?.Retained7552);
            int cost = Cost(baseCost, effects, request.Kind);
            int karma = Integer(root, "karma");
            Need(karma >= cost, "spell_learning_insufficient_karma");
            // SelectionShared.RequirementsMet has no default one-copy limit for spells.
            // Unknown explicit limits are rejected by ValidateSource, never flattened to that default.
            var spells = Box(root, "spells");
            Need(spells.Elements().All(x => x.Name == "spell") && spells.Elements().Count() < 100_000,
                "spell_learning_collection_invalid");
            var expenses = Box(root, "expenses");
            Need(expenses.Elements().All(x => x.Name == "expense") && expenses.Elements().Count() < 100_000,
                "spell_learning_expenses_invalid");
            Guid spellInstance = InstanceId(request, "spell"), expenseId = InstanceId(request, "expense");
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id) && (id == spellInstance || id == expenseId)),
                "spell_learning_instance_identity_conflict");
            string rules = Hash(JsonSerializer.Serialize(new
                { settingsAuthority, source.AuthorityState, magic.EssenceSourceDigest, Semantics = "sr5-spell-learning/v1" }));
            var binding = new CharacterCareerSpellLearningBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest, rules, runtimeDigest);
            var quote = new CharacterCareerSpellLearningQuote(request.SourceId, Scalar(spell, "name"), Scalar(spell, "category"),
                Scalar(spell, "source"), Scalar(spell, "page"), baseCost, effects, cost, karma, checked(karma - cost), magic.Magic,
                traditionId, source.SpellXml, source.TraditionXml);
            Need(IsValidQuote(request, quote), "spell_learning_quote_invalid");
            snapshot = new(binding, quote, context) { Current5703 = learning };
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    // Retain the original read context AND compare with a fresh admission immediately before replacement.
    // Neither a caller-built quote nor a digest copied from a creation draft can authorize learning.
    public static bool IsSourceCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerSpellLearningRequest request, CharacterCareerSpellLearningSnapshot snapshot)
    {
        try
        {
            var owner = snapshot.Binding.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
                ? OwnerScope.LocalSingleUser : new OwnerScope(snapshot.Binding.OwnerId);
            return TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _, snapshot.Current5703)
                && TryReadCurrent5703(owner, saved, request, sources, snapshot.Binding.RuntimeDigest,
                    snapshot.Current5703?.Current, out var fresh, out _)
                && retained!.Binding == snapshot.Binding && fresh!.Binding == snapshot.Binding
                && Same(retained.Quote, snapshot.Quote) && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _, snapshot.Current5703)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void ValidateSource(CharacterCareerSpellLearningRequest request, XElement spell, XElement tradition, Guid traditionId)
    {
        Need(!request.Limited && !request.Extended && !request.Alchemical && !request.Free,
            "spell_learning_variant_unsupported");
        ValidateOrdinarySource(request.SourceId, request.Kind, spell, tradition, traditionId);
    }

    private static void ValidateOrdinarySource(Guid expectedSourceId, CharacterCareerSpellLearningKind kind,
        XElement spell, XElement tradition, Guid traditionId, bool allowExplicitSpellcasting = false)
    {
        Need(spell.Elements().All(e => SpellFields.Contains(e.Name.LocalName) && e.Name.NamespaceName.Length == 0
                && !e.HasAttributes && !e.HasElements), "spell_learning_source_requirements_or_effects_unsupported");
        Need(Guid.TryParse(Scalar(spell, "id"), out var sourceId) && sourceId == expectedSourceId,
            "spell_learning_source_identity_invalid");
        string category = Scalar(spell, "category");
        Need(CatalogKind(category) == kind, "spell_learning_category_mismatch");
        foreach (string field in new[] { "name", "source", "page", "category", "type", "range", "duration", "dv" })
            Need(Scalar(spell, field).Length > 0, "spell_learning_source_shape_invalid");
        string skill = Scalar(spell, "useskill", "");
        Need(skill.Length == 0 || allowExplicitSpellcasting && kind == CharacterCareerSpellLearningKind.Spell
            && skill == "Spellcasting", "spell_learning_alternate_skill_unsupported");
        Need(tradition.Elements().All(e => TraditionFields.Contains(e.Name.LocalName) && e.Name.NamespaceName.Length == 0)
            && Guid.TryParse(Scalar(tradition, "id"), out var id) && id == traditionId
            && Scalar(tradition, "name") != "Custom" && Scalar(tradition, "drain").Length > 0,
            "spell_learning_tradition_semantics_unsupported");
    }

    internal static bool TryValidateCurrentSource(Guid sourceId, CharacterCareerSpellLearningKind kind,
        XElement spell, XElement tradition, Guid traditionId, out string blocker)
    {
        blocker = "spell_learning_source_invalid";
        // A retained current spell may name the same source skill explicitly.
        // Skills admission still requires its source-compiled unlock, and casting
        // independently binds the selector/pool. Ordinary learning keeps its fence.
        try { ValidateOrdinarySource(sourceId, kind, spell, tradition, traditionId,
            allowExplicitSpellcasting: true); blocker = ""; return true; }
        catch (InvalidDataException error) { blocker = error.Message; return false; }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    internal static bool CurrentSourceVisible(XElement settings, XElement spell)
    {
        const string extensionSetting = "extendanydetectionspell";
        bool namedExtended = Scalar(spell, "name").Contains(", Extended", StringComparison.Ordinal);
        // SelectSpell filters only named Extended variants. An ordinary source is
        // visible under either policy, so an absent, inapplicable setting need not
        // be invented. Still validate every supplied setting, and require it when
        // the selected source's visibility actually depends on its value.
        if (Node(settings, extensionSetting) is null && !namedExtended) return true;
        return !Flag(settings, extensionSetting) || !namedExtended;
    }

    internal static bool TryCalculateCurrentRuleCost(int configured,
        IReadOnlyList<CharacterCareerSpellLearningCostEffect> effects,
        CharacterCareerSpellLearningKind kind, out int ruleKarmaCost)
    {
        ruleKarmaCost = 0;
        try
        {
            Need(kind is CharacterCareerSpellLearningKind.Spell or CharacterCareerSpellLearningKind.Ritual,
                "spell_learning_category_mismatch");
            ruleKarmaCost = Cost(configured, effects, kind); return true;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // Caller authenticates the original current improvement graph against retained
    // source definitions. This applies the same Sorcery/category/descriptor gates
    // used by learning, without an owner, request, spend or mutation.
    internal static bool TryEvaluateCurrentEffects(XElement current, XElement spell,
        out IReadOnlyList<CharacterCareerSpellLearningCostEffect> effects, out string blocker,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699? retained = null)
    {
        effects = []; blocker = "spell_learning_effects_invalid";
        try { effects = ReadEffects(current, spell, retained); blocker = ""; return true; }
        catch (InvalidDataException error) { blocker = error.Message; return false; }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // Character.SpellKarmaCost: additive ValueOf, category/global multipliers, StandardRound, nonnegative clamp.
    // Unique/conditional effects need the full stacking consumer and are explicitly unavailable here.
    private static int Cost(int configured, IReadOnlyList<CharacterCareerSpellLearningCostEffect> effects, CharacterCareerSpellLearningKind kind)
    {
        Need(configured >= 0 && effects is not null && effects.Count <= 4096, "spell_learning_cost_invalid");
        string category = kind == CharacterCareerSpellLearningKind.Ritual ? "Rituals" : "Spells";
        decimal value = configured, multiplier = 1m;
        foreach (var effect in effects!)
        {
            Need(effect is not null && effect.Type is "NewSpellKarmaCost" or "NewSpellKarmaCostMultiplier"
                && effect.Category is "" or "Spells" or "Rituals" or "Preparations" or "Enchantments",
                "spell_learning_cost_effect_invalid");
            if (effect!.Category.Length != 0 && effect.Category != category) continue;
            if (effect.Type == "NewSpellKarmaCost") value = checked(value + effect.Value);
            else multiplier = checked(multiplier * (effect.Value / 100m));
        }
        value = checked(value * multiplier);
        // DecimalExtensions.StandardRound rounds all fractions away from zero, not only ties.
        return Math.Max(decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)), 0);
    }

    private static IReadOnlyList<CharacterCareerSpellLearningCostEffect> ReadEffects(XElement root, XElement spell,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699? retained = null)
    {
        Need(retained is null || retained.IsCurrent && retained.Owners!.MatchesCurrentSpellLearningEffects7552(root),
            "spell_learning_retained_focus_authority_changed");
        var costs = new List<CharacterCareerSpellLearningCostEffect>();
        var restrictions = new List<(string Type, string Target)>();
        bool hasSorcery = false;
        foreach (var effect in Box(root, "improvements").Elements())
        {
            Need(effect.Name == "improvement", "spell_learning_effect_shape_invalid");
            if (!Flag(effect, "enabled")) continue;
            string type = Scalar(effect, "improvementttype"), target = Scalar(effect, "improvedname", "");
            if (type == "SpecialSkills")
            {
                // AddImprovementCollection.unlockskills emits the selected SkillsSection.FilterOption.
                // A magician flag alone also describes aspected Conjuring/Enchanting characters.
                ValidateNeutralEffect(effect);
                Need(Scalar(effect, "unique", "").Length == 0, "spell_learning_talent_effect_unsupported");
                if (target is "Magician" or "Sorcery") hasSorcery = true;
                else Need(target is "Conjuring" or "Enchanting" or "Astral" or "Adept" or "Resonance",
                    "spell_learning_talent_filter_unsupported");
                continue;
            }
            if (type == "SpecialTab" && target.Equals("Magician", StringComparison.OrdinalIgnoreCase))
            {
                ValidateNeutralEffect(effect);
                Need(Scalar(effect, "unique", "").Equals("enabletab", StringComparison.OrdinalIgnoreCase),
                    "spell_learning_talent_effect_unsupported");
                continue;
            }
            if (type is "NewSpellKarmaCost" or "NewSpellKarmaCostMultiplier"
                or "AllowSpellCategory" or "AllowSpellRange" or "LimitSpellCategory"
                or "LimitSpellDescriptor" or "BlockSpellDescriptor")
            {
                ValidateNeutralEffect(effect);
                Need(Scalar(effect, "unique", "").Length == 0, "spell_learning_conditional_or_unique_effect_unsupported");
                if (type is "NewSpellKarmaCost" or "NewSpellKarmaCostMultiplier") costs.Add(new(type, target, Number(effect, "val")));
                else
                {
                    Need(target.Length > 0, "spell_learning_constraint_target_invalid");
                    restrictions.Add((type, target));
                }
                continue;
            }
            // The retained compiler proves the complete owned/bonded focus and exact
            // conditional row. Its casting-only contribution stays in the graph for
            // CapturePlayCasting5296; it changes neither learning cost nor eligibility.
            // No source/type label alone grants this exception.
            if (type == "SpellCategory" && retained?.Owners is { } owners
                && owners.IsRetainedSpellcastingFocus7471(effect)) continue;
            Need(!type.Contains("Spell", StringComparison.OrdinalIgnoreCase)
                && type is not ("SkillDisable" or "SkillGroupDisable" or "Skillwire"),
                "spell_learning_effect_semantics_unsupported");
            Need(type != "SpecialTab" || !target.Equals("Magic", StringComparison.OrdinalIgnoreCase), "spell_learning_talent_effect_unsupported");
        }
        Need(hasSorcery, "spell_learning_requires_sorcery_talent");
        string category = Scalar(spell, "category"), range = Scalar(spell, "range"), descriptors = Scalar(spell, "descriptor", "");
        // SelectSpell.BuildSpellList checks allow overrides before category/descriptor limits.
        bool allowed = restrictions.Any(r => r.Type == "AllowSpellCategory" && r.Target == category
            || r.Type == "AllowSpellRange" && r.Target == range);
        if (!allowed)
        {
            Need(!restrictions.Any(r => r.Type == "LimitSpellCategory" && r.Target != category), "spell_learning_category_restricted");
            var limits = restrictions.Where(r => r.Type == "LimitSpellDescriptor").ToArray();
            Need(limits.Length == 0 || limits.Any(r => descriptors.Contains(r.Target, StringComparison.Ordinal)), "spell_learning_descriptor_required");
            Need(!restrictions.Any(r => r.Type == "BlockSpellDescriptor" && descriptors.Contains(r.Target, StringComparison.Ordinal)), "spell_learning_descriptor_blocked");
        }
        Need(retained is null || retained.IsCurrent && retained.Owners!.MatchesCurrentSpellLearningEffects7552(root),
            "spell_learning_retained_focus_authority_changed");
        return Array.AsReadOnly(costs.ToArray());
    }

    private static void ValidateNeutralEffect(XElement effect)
    {
        Need(effect.Elements().All(e => EffectFields.Contains(e.Name.LocalName)), "spell_learning_effect_shape_unsupported");
        foreach (string field in new[] { "condition", "exclude", "target", "customname", "customid", "customgroup" })
            Need(Scalar(effect, field, "").Length == 0, "spell_learning_conditional_or_unique_effect_unsupported");
        Need(!Flag(effect, "custom", false), "spell_learning_custom_effect_unsupported");
        foreach (string field in new[] { "min", "max", "aug", "augmax", "addtorating", "order" })
            Need(Number(effect, field, 0) == 0, "spell_learning_cost_or_constraint_effect_unsupported");
    }

    private static bool IsValidQuote(CharacterCareerSpellLearningRequest request, CharacterCareerSpellLearningQuote? quote)
    {
        if (quote is null || quote.SourceId != request.SourceId || quote.EffectiveMagic <= 0 || quote.TraditionSourceId == Guid.Empty
            || quote.KarmaCost < 0 || quote.KarmaBefore < quote.KarmaCost || quote.KarmaAfter != quote.KarmaBefore - quote.KarmaCost
            || quote.SpellXml is not { Length: > 0 and <= 262144 } || quote.TraditionXml is not { Length: > 0 and <= 262144 }) return false;
        var spell = Parse(quote.SpellXml, "spell");
        ValidateSource(request, spell, Parse(quote.TraditionXml, "tradition"), quote.TraditionSourceId);
        return Cost(quote.ProfileKarmaCost, quote.CostEffects, request.Kind) == quote.KarmaCost
            && quote.Name == Scalar(spell, "name") && quote.Category == Scalar(spell, "category")
            && quote.Book == Scalar(spell, "source") && quote.Page == Scalar(spell, "page");
    }

    private static string Apply(string content, CharacterCareerSpellLearningRequest request, CharacterCareerSpellLearningQuote quote)
    {
        var root = Parse(content, "character");
        Need(Integer(root, "karma") == quote.KarmaBefore, "spell_learning_karma_conflict");
        var source = Parse(quote.SpellXml, "spell");
        var spell = new XElement("spell", new XElement("sourceid", request.SourceId.ToString("D")),
            new XElement("guid", InstanceId(request, "spell").ToString("D")));
        foreach (string name in new[] { "name", "category", "type", "range", "damage", "duration", "dv", "useskill", "source", "page" })
            spell.Add(new XElement(name, Scalar(source, name, "")));
        spell.Add(new XElement("descriptors", Scalar(source, "descriptor", "")));
        foreach (string name in new[] { "limited", "extended", "customextended", "alchemical", "freebonus", "barehandedadept" })
            spell.Add(new XElement(name, false));
        spell.Add(new XElement("extra", ""), new XElement("notes", Scalar(source, "altnotes", Scalar(source, "notes", ""))),
            new XElement("notesColor", Scalar(source, "notesColor", "#000000")), new XElement("improvementsource", "Spell"), new XElement("grade", 0));
        var expense = new XElement("expense", new XElement("guid", InstanceId(request, "expense").ToString("D")),
            new XElement("date", XmlConvert.ToString(request.ExpenseDateLocal, XmlDateTimeSerializationMode.Unspecified)),
            new XElement("amount", (-quote.KarmaCost).ToString(Invariant)), new XElement("reason", "Learn Spell: " + quote.Name),
            new XElement("type", "Karma"), new XElement("refund", false), new XElement("forcecareervisible", true));
        // The dedicated transaction owns reversal; legacy generic Undo cannot undo half of a receipt.
        root.SetElementValue("karma", quote.KarmaAfter.ToString(Invariant));
        Append(root, "spells", spell);
        Append(root, "expenses", expense);
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static void Append(XElement root, string name, XElement row)
    {
        var container = Box(root, name);
        if (container.Parent is null) root.Add(container);
        container.Add(row);
    }
    private static XElement Parse(string xml, string name)
    {
        Need(xml.Length is > 0 and <= MaximumContentLength, "spell_learning_xml_size_invalid");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Need(document.Root is { } root && root.Name == name && !root.HasAttributes, "spell_learning_xml_invalid");
        return document.Root!;
    }
    private static XElement? Node(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes), "spell_learning_ambiguous_field");
        return nodes.SingleOrDefault();
    }
    private static XElement Box(XElement parent, string name)
    {
        var node = Node(parent, name) ?? new XElement(name);
        Need(!node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "spell_learning_container_invalid");
        return node;
    }
    private static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var node = Node(parent, name);
        Need(node is not null || fallback is not null, "spell_learning_missing_field_" + name);
        Need(node is null || !node.HasElements, "spell_learning_scalar_invalid");
        return node?.Value ?? fallback!;
    }
    private static decimal Number(XElement parent, string name, decimal? fallback = null)
    {
        Need(decimal.TryParse(Scalar(parent, name, fallback?.ToString(Invariant)),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out var value),
            "spell_learning_number_invalid");
        return value;
    }
    private static int Integer(XElement parent, string name)
    {
        decimal value = Number(parent, name);
        Need(decimal.Truncate(value) == value, "spell_learning_integer_invalid");
        return checked((int)value);
    }
    private static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Scalar(parent, name, fallback?.ToString());
        if (bool.TryParse(value, out bool flag)) return flag;
        Need(value is "0" or "1", "spell_learning_boolean_invalid");
        return value == "1";
    }
    private static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
