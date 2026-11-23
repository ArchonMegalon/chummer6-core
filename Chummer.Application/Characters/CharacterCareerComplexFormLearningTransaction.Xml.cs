using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerComplexFormLearningTransaction
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly HashSet<string> FormFields = new(StringComparer.Ordinal)
        { "id", "name", "page", "source", "target", "duration", "fv", "useskill", "notes", "altnotes", "notesColor", "translate", "altpage" };

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerComplexFormLearningRequest request,
        ICharacterSourceDataResolver sources, string runtimeDigest, out CharacterCareerComplexFormLearningSnapshot? snapshot, out string error)
        => TryReadCurrent5703(owner, saved, request, sources, runtimeDigest, null, out snapshot, out error);

    internal static bool TryReadCurrent5703(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerComplexFormLearningRequest request,
        ICharacterSourceDataResolver sources, string runtimeDigest, CharacterCurrentRuleEnvironmentContext? current, out CharacterCareerComplexFormLearningSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "complex_form_learning_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            var learning = context is null ? null : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(
                saved, current, context, true);
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, out snapshot, out error, learning);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryReadCore(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerComplexFormLearningRequest request,
        ICharacterSourceDataContext context, string runtimeDigest, out CharacterCareerComplexFormLearningSnapshot? snapshot, out string error,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? learning = null)
    {
        snapshot = null;
        error = "complex_form_learning_state_invalid";
        try
        {
            Need(IsValidOwner(owner) && IsValidRequest(request) && saved.Id == request.WorkspaceId
                && saved.ContentRevision is > 0 and < long.MaxValue && saved.SavedRevision == saved.ContentRevision
                && IsDigest(runtimeDigest), "complex_form_learning_workspace_invalid");
            Need(learning is null || learning.Matches(saved, context), "complex_form_learning_current_changed");
            Need(CharacterCareerPowerGovernance.Allows(saved), "complex_form_learning_requires_local_saved_sr5_career");
            Need(CharacterCareerInitiationTransaction.ObserveHistory(saved), "complex_form_learning_submersion_history_inconsistent");
            if (request.CatalogBinding is { } selected)
                Need(TryReadCatalogCore(owner, saved, context, runtimeDigest, out var catalog, out _, learning) && catalog!.Binding == selected,
                    "complex_form_learning_catalog_changed");
            var root = learning is null ? Parse(saved.Document.Content, "character") : new XElement(learning.Graph);
            Need(Flag(root, "created") && !Flag(root, "ignorerules", false), "complex_form_learning_requires_rules_career");
            Need(context.TryResolveCareerSettingsProfile(out var profile, out var settingsAuthority)
                && !string.IsNullOrWhiteSpace(settingsAuthority), "complex_form_learning_profile_unavailable");
            var settings = Parse(profile, "settings");
            Need(CharacterCareerSettingsProfileBinding.MatchesSelectedProfile(Scalar(root, "settings"), settings, context, "complex_form_learning"),
                "complex_form_learning_profile_binding_invalid");
            var resonance = learning is null ? CharacterCareerResonanceEvaluator.Evaluate(root, context)
                : learning.Resonance ?? throw new InvalidDataException("complex_form_learning_imported_resonance_unavailable");
            Need(resonance.IsSupported, resonance.Blockers.FirstOrDefault() ?? "complex_form_learning_resonance_unavailable");
            Need(resonance.IsTechnomancer && resonance.EffectiveResonance > 0, "complex_form_learning_requires_technomancer");
            Need(context is ICharacterCareerComplexFormLearningSourceContext, "complex_form_learning_source_capability_unavailable");
            Need(((ICharacterCareerComplexFormLearningSourceContext)context).TryResolveCareerComplexFormLearningSource(request.SourceId, out var source)
                && source is not null && !string.IsNullOrWhiteSpace(source.AuthorityState), "complex_form_learning_source_unavailable");
            var form = Parse(source!.ComplexFormXml, "complexform");
            ValidateSource(request, form);
            int configured = Integer(Box(settings, "karmacost"), "karmanewcomplexform");
            Need(configured >= 0, "complex_form_learning_profile_cost_invalid");
            int cost = Cost(configured, resonance.CostEffects);
            int karma = Integer(root, "karma");
            Need(karma >= cost, "complex_form_learning_insufficient_karma");
            var owned = Box(root, "complexforms");
            Need(owned.Elements().All(e => e.Name == "complexform") && owned.Elements().Count() < 100_000,
                "complex_form_learning_collection_invalid");
            foreach (var row in owned.Elements())
            {
                Need(Guid.TryParse(Scalar(row, "sourceid"), out var id) && id != Guid.Empty,
                    "complex_form_learning_owned_source_identity_invalid");
                // This is a slice boundary, not a claimed canonical blanket duplicate prohibition.
                Need(id != request.SourceId && Scalar(row, "name") != Scalar(form, "name"), "complex_form_learning_repeat_unsupported_by_slice");
            }
            int count = owned.Elements().Count();
            decimal limit = CurrentLimit(resonance);
            bool ignore = Flag(settings, "ignorecomplexformlimit");
            Need(ignore || count < limit, "complex_form_learning_count_limit");
            var expenses = Box(root, "expenses");
            Need(expenses.Elements().All(e => e.Name == "expense") && expenses.Elements().Count() < 100_000,
                "complex_form_learning_expenses_invalid");
            Guid formId = InstanceId(request, "complexform"), expenseId = InstanceId(request, "expense");
            Need(!root.Descendants("guid").Any(e => Guid.TryParse(e.Value, out var id) && (id == formId || id == expenseId)),
                "complex_form_learning_instance_identity_conflict");
            string rules = Hash(JsonSerializer.Serialize(new { settingsAuthority, source.AuthorityState,
                resonance.SourceDigest, Semantics = "sr5-complex-form-learning/v1" }));
            var binding = new CharacterCareerComplexFormLearningBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest, rules, runtimeDigest);
            var quote = new CharacterCareerComplexFormLearningQuote(request.SourceId, Scalar(form, "name"), Scalar(form, "source"),
                Scalar(form, "page"), configured, resonance.CostEffects, cost, karma, checked(karma - cost), resonance.EffectiveResonance,
                count, limit, ignore, source.ComplexFormXml);
            Need(IsValidQuote(request, quote), "complex_form_learning_quote_invalid");
            snapshot = new(binding, quote, context) { Current5703 = learning };
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    public static bool IsSourceCurrent(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerComplexFormLearningRequest request, CharacterCareerComplexFormLearningSnapshot snapshot)
    {
        try
        {
            // Keep the caller's trusted local scope; a persisted owner ID cannot recreate that capability.
            return IsValidOwner(owner) && owner.NormalizedValue == snapshot.Binding.OwnerId
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _, snapshot.Current5703)
                && TryReadCurrent5703(owner, saved, request, sources, snapshot.Binding.RuntimeDigest,
                    snapshot.Current5703?.Current, out var fresh, out _)
                && retained!.Binding == snapshot.Binding && fresh!.Binding == snapshot.Binding
                && Same(retained.Quote, snapshot.Quote) && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _, snapshot.Current5703)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void ValidateSource(CharacterCareerComplexFormLearningRequest request, XElement form)
        => ValidateOrdinarySource(request.SourceId, form);

    private static void ValidateOrdinarySource(Guid sourceId, XElement form)
    {
        Need(form.Elements().All(e => FormFields.Contains(e.Name.LocalName) && e.Name.NamespaceName.Length == 0
            && !e.HasAttributes && !e.HasElements), "complex_form_learning_source_choices_requirements_or_limits_unsupported");
        Need(Guid.TryParse(Scalar(form, "id"), out var id) && id == sourceId, "complex_form_learning_source_identity_invalid");
        foreach (string field in new[] { "name", "source", "page", "target", "duration", "fv" })
            Need(Scalar(form, field).Length > 0, "complex_form_learning_source_shape_invalid");
        Need(Scalar(form, "useskill", "").Length == 0, "complex_form_learning_alternate_skill_unsupported");
    }

    internal static bool TryValidateCurrentSource(Guid sourceId, XElement form, out string blocker)
    {
        blocker = "complex_form_learning_source_invalid";
        try { ValidateOrdinarySource(sourceId, form); blocker = ""; return true; }
        catch (InvalidDataException error) { blocker = error.Message; return false; }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    internal static bool TryCalculateCurrentRuleCost(int configured,
        IReadOnlyList<CharacterCareerComplexFormLearningCostEffect> effects, out int ruleKarmaCost)
    {
        ruleKarmaCost = 0;
        try { ruleKarmaCost = Cost(configured, effects); return true; }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    private static decimal CurrentLimit(CharacterCareerResonanceEvaluation resonance)
        => checked(2m * resonance.EffectiveResonance + resonance.LimitModifier);

    internal static bool TryEvaluateCurrentCount(XElement settings, CharacterCareerResonanceEvaluation resonance,
        int ownedCount, bool adding, out decimal limit)
    {
        limit = 0;
        try
        {
            Need(resonance.IsSupported && ownedCount >= 0, "complex_form_learning_count_invalid");
            limit = CurrentLimit(resonance);
            return Flag(settings, "ignorecomplexformlimit") || (adding ? ownedCount < limit : ownedCount <= limit);
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // Character.ComplexFormKarmaCost + ImprovementManager.ValueOf: only authenticated, unconditional,
    // non-unique effects enter here. Native StandardRound is Ceiling/Floor for ALL fractions.
    private static int Cost(int configured, IReadOnlyList<CharacterCareerComplexFormLearningCostEffect> effects)
    {
        Need(configured >= 0 && effects is not null && effects.Count <= 4096, "complex_form_learning_cost_invalid");
        decimal value = configured, multiplier = 1m;
        foreach (var effect in effects!)
        {
            Need(effect is not null && effect.Type is "NewComplexFormKarmaCost" or "NewComplexFormKarmaCostMultiplier",
                "complex_form_learning_cost_effect_invalid");
            if (effect!.Type == "NewComplexFormKarmaCost") value = checked(value + effect.Value);
            else multiplier = checked(multiplier * (effect.Value / 100m));
        }
        value = checked(value * multiplier);
        return Math.Max(decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)), 0);
    }

    private static bool IsValidQuote(CharacterCareerComplexFormLearningRequest request, CharacterCareerComplexFormLearningQuote? quote)
    {
        if (quote is null || quote.SourceId != request.SourceId || quote.EffectiveResonance <= 0 || quote.OwnedFormCount < 0
            || !quote.IgnoreComplexFormLimit && quote.OwnedFormCount >= quote.ComplexFormLimit
            || quote.KarmaCost < 0 || quote.KarmaBefore < quote.KarmaCost || quote.KarmaAfter != quote.KarmaBefore - quote.KarmaCost
            || quote.ComplexFormXml is not { Length: > 0 and <= 262144 }) return false;
        var form = Parse(quote.ComplexFormXml, "complexform");
        ValidateSource(request, form);
        return Cost(quote.ProfileKarmaCost, quote.CostEffects) == quote.KarmaCost && quote.Name == Scalar(form, "name")
            && quote.Book == Scalar(form, "source") && quote.Page == Scalar(form, "page");
    }

    private static string Apply(string content, CharacterCareerComplexFormLearningRequest request, CharacterCareerComplexFormLearningQuote quote)
    {
        var root = Parse(content, "character");
        Need(Integer(root, "karma") == quote.KarmaBefore, "complex_form_learning_karma_conflict");
        var source = Parse(quote.ComplexFormXml, "complexform");
        var form = new XElement("complexform", new XElement("sourceid", request.SourceId.ToString("D")),
            new XElement("guid", InstanceId(request, "complexform").ToString("D")));
        foreach (string name in new[] { "name", "useskill", "target", "duration", "fv" }) form.Add(new XElement(name, Scalar(source, name, "")));
        form.Add(new XElement("extra", ""), new XElement("source", quote.Book), new XElement("page", quote.Page),
            new XElement("notes", Scalar(source, "altnotes", Scalar(source, "notes", ""))),
            new XElement("notesColor", Scalar(source, "notesColor", "#000000")), new XElement("grade", 0));
        var expense = new XElement("expense", new XElement("guid", InstanceId(request, "expense").ToString("D")),
            new XElement("date", request.ExpenseDateLocal.ToString("s", Invariant)),
            new XElement("amount", (-quote.KarmaCost).ToString(Invariant)), new XElement("reason", "Learned Complex Form " + quote.Name),
            new XElement("type", "Karma"), new XElement("refund", false), new XElement("forcecareervisible", false));
        // Reversal is out of scope: legacy generic Undo must not split a durable receipt.
        root.SetElementValue("karma", quote.KarmaAfter.ToString(Invariant));
        Append(root, "complexforms", form);
        Append(root, "expenses", expense);
        return root.ToString(SaveOptions.DisableFormatting);
    }
    private static void Append(XElement root, string name, XElement row)
    {
        var container = Box(root, name);
        if (container.Parent is null) root.Add(container);
        container.Add(row);
    }
    internal static XElement Parse(string xml, string name)
    {
        Need(xml.Length is > 0 and <= MaximumContentLength, "complex_form_learning_xml_size_invalid");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Need(document.Root is { } root && root.Name == name && !root.HasAttributes, "complex_form_learning_xml_invalid");
        return document.Root!;
    }
    internal static XElement? Node(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes), "complex_form_learning_ambiguous_field");
        return nodes.SingleOrDefault();
    }
    internal static XElement Box(XElement parent, string name)
    {
        var node = Node(parent, name) ?? new XElement(name);
        Need(!node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "complex_form_learning_container_invalid");
        return node;
    }
    internal static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var node = Node(parent, name);
        Need(node is not null || fallback is not null, "complex_form_learning_missing_field_" + name);
        Need(node is null || !node.HasElements, "complex_form_learning_scalar_invalid");
        return node?.Value ?? fallback!;
    }
    internal static decimal Number(XElement parent, string name, decimal? fallback = null)
    {
        Need(decimal.TryParse(Scalar(parent, name, fallback?.ToString(Invariant)),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out var value),
            "complex_form_learning_number_invalid");
        return value;
    }
    internal static int Integer(XElement parent, string name)
    {
        decimal value = Number(parent, name);
        Need(decimal.Truncate(value) == value, "complex_form_learning_integer_invalid");
        return checked((int)value);
    }
    internal static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Scalar(parent, name, fallback?.ToString());
        if (bool.TryParse(value, out bool flag)) return flag;
        Need(value is "0" or "1", "complex_form_learning_boolean_invalid");
        return value == "1";
    }
    internal static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
