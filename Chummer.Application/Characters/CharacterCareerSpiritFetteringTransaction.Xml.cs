using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerSpiritFetteringTransaction
{
    public static bool TryRead(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerSpiritFetteringRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerSpiritFetteringSnapshot? snapshot, out string error)
        => TryReadCurrent5724(owner, saved, request, sources, runtimeDigest, null, out snapshot, out error);

    internal static bool TryReadCurrent5724(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerSpiritFetteringRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        CharacterCurrentRuleEnvironmentContext? current,
        out CharacterCareerSpiritFetteringSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "spirit_fettering_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            var root = Parse(saved.Document.Content, "character");
            var entity = ReadSpirits(root).Single(s => s.SpiritId == request.SpiritId);
            var proof = context is null ? null : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(
                saved, current, context, entity.EntityType == "Sprite");
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, out snapshot, out error, proof);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool TryReadCore(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerSpiritFetteringRequest request, ICharacterSourceDataContext context, string runtimeDigest,
        out CharacterCareerSpiritFetteringSnapshot? snapshot, out string error,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? proof = null)
    {
        snapshot = null; error = "spirit_fettering_state_invalid";
        try
        {
            Need(owner.IsValid && IsValidRequest(request) && request.WorkspaceId == saved.Id
                && saved.SavedRevision == saved.ContentRevision && IsValidHistory(saved)
                && CharacterCareerPowerGovernance.Allows(saved) && IsDigest(runtimeDigest), "spirit_fettering_workspace_unavailable");
            Need(proof is null || proof.Matches(saved, context), "spirit_fettering_current_changed");
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created"), "spirit_fettering_career_only");
            Need(context.TryResolveCareerSettingsProfile(out string profile, out string authority)
                && !string.IsNullOrWhiteSpace(authority), "spirit_fettering_profile_unavailable");
            var settings = Parse(profile, "settings");
            Need(Scalar(root, "settings") == Scalar(settings, "id"), "spirit_fettering_profile_binding_invalid");
            int multiplier = Integer(Box(settings, "karmacost"), "karmaspiritfettering");
            Need(multiplier >= 0, "spirit_fettering_profile_cost_invalid");
            var spirits = ReadSpirits(root);
            var selected = Box(root, "spirits").Elements("spirit").Single(x => Identity(x) == request.SpiritId);
            // CharacterCareer exposes panSpirits only with MagicianEnabled and the Sprite tab only
            // with TechnomancerEnabled. Character.Save/Load persist these exact flags.
            var selectedBasis = spirits.Single(x => x.SpiritId == request.SpiritId);
            Need(selectedBasis.EntityType is "Spirit" or "Sprite"
                && Flag(root, selectedBasis.EntityType == "Spirit" ? "magician" : "technomancer", false),
                "spirit_fettering_parent_eligibility_unavailable");
            if (proof is not null)
                Need(selectedBasis.EntityType == "Sprite"
                    ? proof.Resonance is { IsSupported: true, IsTechnomancer: true }
                    : proof.Magic is { IsSupported: true }, "spirit_fettering_imported_talent_unavailable");
            var improvements = Box(root, "improvements");
            Need(improvements.Elements().All(x => x.Name == "improvement" && !x.HasAttributes), "spirit_fettering_effect_graph_invalid");
            bool allowed = false; int count = 0;
            foreach (var improvement in improvements.Elements())
            {
                bool enabled = Enabled(improvement);
                if (Scalar(improvement, "improvementttype", "") == "AllowSpriteFettering" && enabled) allowed = true;
                if (Scalar(improvement, "improvementsource", "") != "SpiritFettering") continue;
                Need(IsCanonicalSpiritFetteringImprovement(improvement), "spirit_fettering_owned_effect_invalid");
                count++;
            }
            int karma = Integer(root, "karma");
            Need(CharacterSpiritFetteringRules.TryProject(request.SpiritId, true, karma, multiplier, allowed, count, spirits, out var state)
                && state is not null && (request.RequestedName is null
                    ? CharacterSpiritFetteringRules.CanSet(state, request.Fettered) : request.Fettered == state.Fettered), "spirit_fettering_operation_not_allowed");
            string name = Scalar(selected, "name");
            string? nameSelection = null;
            if (request.RequestedName is { } requestedName)
            {
                var tradition = Box(root, "tradition");
                string type = selectedBasis.EntityType;
                Need(context.TryResolveTraditionSpiritNames(type, Scalar(tradition, "sourceid"), out var names),
                    "spirit_name_selected_source_unavailable");
                Need(context.TryResolveSpiritCatalogNames(type, out var catalog)
                    && catalog.Count(n => n == requestedName) == 1
                    && CharacterSpiritNameChoiceRules.TryProject(request.SpiritId, spirits.Select(s => s.SpiritId).ToArray(),
                        type, name, names, [], Flag(root, "magenabled", false), Flag(root, "resenabled", false), [], [], out var choice)
                    && choice is not null && CharacterSpiritNameChoiceRules.CanSet(choice, requestedName),
                    "spirit_name_selected_source_unavailable");
                nameSelection = Hash(JsonSerializer.Serialize(new { type, SourceId = Scalar(tradition, "sourceid"), names, catalog }));
                Need(!improvements.Elements().Any(e => Enabled(e) && Scalar(e, "improvementttype", "")
                    is "AddSpirit" or "AddSprite" or "LimitSpiritCategory"), "spirit_name_source_effects_unsupported");
            }
            bool changed = request.RequestedName is { } targetName ? name != targetName : state!.Fettered != request.Fettered;
            int cost = request.RequestedName is null && changed && request.Fettered ? state!.ActivationKarmaCost : 0;
            var expenses = Box(root, "expenses");
            Need(expenses.Elements().All(x => x.Name == "expense"), "spirit_fettering_expenses_invalid");
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id) && id == ExpenseId(request)), "spirit_fettering_expense_identity_conflict");
            var quote = new CharacterCareerSpiritFetteringQuote(state!, name, multiplier,
                cost, karma, checked(karma - cost), changed) { TargetName = request.RequestedName };
            Need(IsValidQuote(request, quote), "spirit_fettering_quote_invalid");
            var binding = new CharacterCareerSpiritFetteringBinding(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser,
                owner.AuthorityInstanceId, owner.TransitionRevision, saved.Id, saved.ContentRevision, saved.LocalHistory!.IncarnationId,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest,
                Hash(JsonSerializer.Serialize(new { profile, authority, Semantics = "sr5-spirit-fettering/v1", Retained = proof?.Current.Capture.IntegrityDigest, NameSelection = nameSelection })), runtimeDigest);
            snapshot = new(binding, quote, context) { Current5724 = proof }; error = ""; return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool IsSourceCurrent(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerSpiritFetteringRequest request, CharacterCareerSpiritFetteringSnapshot snapshot)
    {
        try
        {
            return TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _, snapshot.Current5724)
                && TryReadCurrent5724(owner, saved, request, sources, snapshot.Binding.RuntimeDigest,
                    snapshot.Current5724?.Current, out var fresh, out _)
                && retained!.Binding == snapshot.Binding && fresh!.Binding == snapshot.Binding
                && Same(retained.Quote, snapshot.Quote) && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _, snapshot.Current5724)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static CharacterSpiritFetteringBasis[] ReadSpirits(XElement root)
    {
        var container = Box(root, "spirits");
        Need(container.Elements().All(x => x.Name == "spirit" && !x.HasAttributes), "spirit_fettering_collection_invalid");
        return container.Elements().Select(x => new CharacterSpiritFetteringBasis(Identity(x),
            Scalar(x, "type").Trim().ToUpperInvariant() switch { "SPIRIT" => "Spirit", "SPRITE" => "Sprite", _ => "" },
            Integer(x, "force", 1), Integer(x, "services", 0), Flag(x, "bound", true), Flag(x, "fettered", false))).ToArray();
    }
    public static IReadOnlyList<CharacterCareerSpiritFetteringEntry> ReadEntries(string xml)
    {
        var root = Parse(xml, "character");
        Need(Flag(root, "created"), "spirit_fettering_career_only");
        var rows = ReadSpirits(root);
        Need(rows.All(x => x.SpiritId != Guid.Empty && x.EntityType is "Spirit" or "Sprite" && x.Force >= 0 && x.Services >= 0)
            && rows.Select(x => x.SpiritId).Distinct().Count() == rows.Length, "spirit_fettering_collection_invalid");
        return rows.Select(x => new CharacterCareerSpiritFetteringEntry(x.SpiritId,
            Scalar(Box(root, "spirits").Elements().Single(e => Identity(e) == x.SpiritId), "name"), x.EntityType, x.Fettered)).ToArray();
    }
    private static Guid Identity(XElement node)
    {
        Need(Guid.TryParseExact(Scalar(node, "guid"), "D", out var id) && id != Guid.Empty, "spirit_fettering_identity_invalid");
        return id;
    }
    private static string Apply(string content, CharacterCareerSpiritFetteringRequest request, CharacterCareerSpiritFetteringQuote quote)
    {
        if (!quote.ChangesState) return content;
        var root = Parse(content, "character");
        var spirit = Box(root, "spirits").Elements("spirit").Single(x => Identity(x) == request.SpiritId);
        if (request.RequestedName is { } requestedName)
        {
            Set(spirit, "name", requestedName);
            return root.Document!.ToString(SaveOptions.DisableFormatting);
        }
        Set(spirit, "fettered", request.Fettered ? "True" : "False");
        if (quote.State.EntityType == "Spirit")
        {
            var improvements = Box(root, "improvements");
            if (request.Fettered) improvements.Add(CreateSpiritFetteringImprovement());
            else improvements.Elements("improvement").Single(IsCanonicalSpiritFetteringImprovement).Remove();
        }
        if (request.Fettered)
        {
            Set(root, "karma", quote.KarmaAfter.ToString(CultureInfo.InvariantCulture));
            Box(root, "expenses").Add(CreateSpiritFetteringExpense(quote.State, quote.Name, ExpenseId(request), request.ExpenseDateLocal));
        }
        return root.Document!.ToString(SaveOptions.DisableFormatting);
    }
    // Extracted from the delivered Presentation serializer, shared by Creation and Career.
    public static XElement CreateSpiritFetteringImprovement()
        => new(
            "improvement",
            new XElement("target"),
            new XElement("improvedname", "MAG"),
            new XElement("sourcename"),
            new XElement("min", "0"),
            new XElement("max", "0"),
            new XElement("aug", "-1"),
            new XElement("augmax", "0"),
            new XElement("val", "0"),
            new XElement("rating", "1"),
            new XElement("exclude"),
            new XElement("condition"),
            new XElement("improvementttype", "Attribute"),
            new XElement("improvementsource", "SpiritFettering"),
            new XElement("custom", "False"),
            new XElement("customname"),
            new XElement("customid"),
            new XElement("customgroup"),
            new XElement("addtorating", "0"),
            new XElement("enabled", "1"),
            new XElement("order", "0"),
            new XElement("notes"));

    public static XElement CreateSpiritFetteringExpense(CharacterSpiritFetteringState state, string name, Guid expenseId, DateTime date)
        => new("expense", new XElement("guid", expenseId.ToString("D")),
            new XElement("date", date.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", (-state.ActivationKarmaCost).ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", $"Fettered Spirit {name}"), new XElement("type", "Karma"),
            new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
            new XElement("undo", new XElement("karmatype", "SpiritFettering"), new XElement("nuyentype", "AddCyberware"),
                new XElement("objectid", state.SpiritId.ToString("D")), new XElement("qty", "0"), new XElement("extra")));
    private static bool IsCanonicalSpiritFetteringImprovement(XElement node)
    {
        var expected = CreateSpiritFetteringImprovement();
        if (node.Name != "improvement" || node.HasAttributes || node.Elements().Any(x => x.HasElements || x.HasAttributes)
            || node.Elements().GroupBy(x => x.Name).Any(x => x.Count() != 1)) return false;
        foreach (var field in expected.Elements())
        {
            string name = field.Name.LocalName;
            if (name == "notes") continue;
            string actual = Scalar(node, name, "");
            if (name == "enabled") { if (Enabled(node) != Enabled(expected)) return false; }
            else if (name == "custom") { if (Flag(node, name) != Flag(expected, name)) return false; }
            else if (decimal.TryParse(field.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numeric))
            { if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value != numeric) return false; }
            else if (actual != field.Value) return false;
        }
        return node.Elements().All(x => expected.Element(x.Name) is not null || x.Name == "notesColor" || x.Name == "unique" && x.Value.Length == 0);
    }
    private static XElement Parse(string xml, string name)
    {
        Need(xml.Length is > 0 and <= 67_108_864, "spirit_fettering_xml_size_invalid");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Need(doc.Root is { } root && root.Name == name && !root.HasAttributes, "spirit_fettering_xml_invalid");
        return doc.Root!;
    }
    private static XElement? Node(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(x => x.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes), "spirit_fettering_ambiguous_field");
        return nodes.SingleOrDefault();
    }
    private static XElement Box(XElement parent, string name)
    {
        var node = Node(parent, name); Need(node is not null && !node.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value)), "spirit_fettering_missing_container_" + name);
        return node!;
    }
    private static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var node = Node(parent, name); Need(node is not null || fallback is not null, "spirit_fettering_missing_field_" + name);
        Need(node is null || !node.HasElements, "spirit_fettering_scalar_invalid"); return node?.Value.Trim() ?? fallback!;
    }
    private static int Integer(XElement parent, string name, int? fallback = null)
    {
        Need(int.TryParse(Scalar(parent, name, fallback?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value), "spirit_fettering_integer_invalid"); return value;
    }
    private static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Scalar(parent, name, fallback?.ToString());
        if (bool.TryParse(value, out bool flag)) return flag;
        throw new InvalidDataException("spirit_fettering_boolean_invalid");
    }
    private static bool Enabled(XElement improvement)
    {
        string value = Scalar(improvement, "enabled", "True");
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return number > 0;
        if (bool.TryParse(value, out bool enabled)) return enabled;
        throw new InvalidDataException("spirit_fettering_effect_enabled_invalid");
    }
    private static void Set(XElement parent, string name, string value)
    { var node = Node(parent, name); if (node is null) parent.Add(new XElement(name, value)); else node.Value = value; }
    private static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
