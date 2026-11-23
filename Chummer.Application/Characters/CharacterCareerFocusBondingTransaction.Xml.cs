using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerFocusBondingTransaction
{
    public static bool TryRead(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerFocusBondingRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerFocusBondingSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "focus_bonding_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, out snapshot, out error);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryReadCore(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerFocusBondingRequest request, ICharacterSourceDataContext context, string runtimeDigest,
        out CharacterCareerFocusBondingSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "focus_bonding_state_invalid";
        try
        {
            Need(owner.IsValid && IsValidRequest(request) && request.WorkspaceId == saved.Id
                && saved.SavedRevision == saved.ContentRevision && IsValidHistory(saved)
                && CharacterCareerPowerGovernance.Allows(saved) && IsDigest(runtimeDigest), "focus_bonding_workspace_unavailable");
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created"), "focus_bonding_career_only");
            // CharacterCareer exposes the magician tab for MagicianEnabled OR AdeptEnabled.
            Need(Flag(root, "magician", false) || Flag(root, "adept", false), "focus_bonding_parent_eligibility_unavailable");
            Need(context.TryResolveCareerSettingsProfile(out string profile, out string authority)
                && !string.IsNullOrWhiteSpace(authority), "focus_bonding_profile_unavailable");
            var settings = Parse(profile, "settings");
            Need(MatchesSelectedProfile(root, settings, context), "focus_bonding_profile_binding_invalid");
            var inventory = ReadInventory(root);
            var gear = inventory.Gear.Single(x => Identity(x) == request.GearId);
            Need(gear.Parent == Node(root, "gears"), "focus_bonding_nested_owner_unsupported");
            var sourceId = GuidValue(gear, "sourceid");
            Need(context.TryResolveCareerGearPurchaseSource(sourceId, out var source) && source is not null,
                "focus_bonding_source_unavailable");
            Need(source!.SourceId == sourceId && IsSourceDigest(source.CatalogDigest)
                && IsSourceDigest(source.ProfileDigest) && IsSourceDigest(source.CustomDataDigest), "focus_bonding_source_binding_invalid");
            var sourceNode = Parse(source.SourceXml, "gear");
            var effects = CompileFocus(gear, sourceNode);
            Need(source.Name == Scalar(gear, "name") && source.SourceBook == Scalar(gear, "source"),
                "focus_bonding_source_binding_invalid");
            var ownedEffects = OwnedEffects(root, request.GearId);
            bool bonded = Flag(gear, "bonded"), equipped = Flag(gear, "equipped");
            Need(bonded ? EquivalentEffects(ownedEffects, effects) : ownedEffects.Length == 0,
                "focus_bonding_owned_effect_drift");
            Need(context.TryResolveCareerQualityMetatype(out var metatypeXml, out var metatypeAuthority)
                && !string.IsNullOrWhiteSpace(metatypeAuthority), "focus_bonding_metatype_unavailable");
            var metatype = Parse(metatypeXml, "metatype");
            // Resolve saved aliases without rewriting the imported document. The source
            // resolver still owns unique catalog/name resolution and source currentness.
            Need(CharacterMetatypeIdentity.TryRead(root, out var metatypeName, out var metatypeId,
                    out var variantName, out var variantId)
                && (metatypeId is null || metatypeId == GuidValue(metatype, "id"))
                && metatypeName == Scalar(metatype, "name")
                && (variantId is null || variantId == Guid.Empty) && variantName is "" or "None",
                "focus_bonding_metatype_binding_invalid");
            var magic = CharacterCareerMagicEvaluator.Evaluate(root, settings, metatype, context);
            Need(magic.IsSupported, "focus_bonding_magic_unsupported:" + string.Join(',', magic.Blockers));
            var focus = inventory.Foci.SingleOrDefault(x => GuidValue(x, "gearid") == request.GearId);
            var state = new CharacterCareerFocusBondingState(request.GearId, sourceId, focus is null ? null : Identity(focus),
                Scalar(gear, "name"), Scalar(gear, "extra"), Integer(gear, "rating"), bonded, equipped,
                inventory.Count, inventory.Force, magic.Magic, magic.AdeptMagic, Flag(root, "ignorerules", false));
            Need(CharacterCareerFocusBondingRules.Allows(state, request.Bonded), "focus_bonding_limit_exceeded");
            // Career resolves <settings>; the Creation price reader accepts <setting>.
            // Preserve the complete selected profile and adapt only a private copy's wrapper.
            var pricingProfile = new XElement(settings) { Name = "setting" };
            Need(CharacterCreationFocusKarma1220.TryPriceOwnedOrdinary(pricingProfile, root, gear, out var price, out int bindingKarma),
                "focus_bonding_price_unavailable");
            bool changed = bonded != request.Bonded;
            int karma = Integer(root, "karma"), cost = changed && request.Bonded ? bindingKarma : 0;
            Need(!changed || !request.Bonded || cost <= karma, "focus_bonding_insufficient_karma");
            Need(Box(root, "expenses").Elements().All(x => x.Name == "expense"), "focus_bonding_expenses_invalid");
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id)
                && (id == ExpenseId(request) || id == BindingId(request))), "focus_bonding_identity_conflict");
            var quote = new CharacterCareerFocusBondingQuote(state, price!.Field, price.Price, price.Multiplier, price.Addition,
                bindingKarma, cost, karma, checked(karma - cost), changed,
                Hash(sourceNode.ToString(SaveOptions.DisableFormatting)), Hash(string.Join('\0', ownedEffects.Select(EffectKey))));
            Need(IsValidQuote(request, quote), "focus_bonding_quote_invalid");
            var binding = new CharacterCareerFocusBondingBinding(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser,
                owner.AuthorityInstanceId, owner.TransitionRevision, saved.Id, saved.ContentRevision, saved.LocalHistory!.IncarnationId,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest,
                Hash(JsonSerializer.Serialize(new { profile, authority, source, metatypeXml, metatypeAuthority,
                    magic.EssenceSourceDigest, Semantics = "sr5-focus-bonding/v1" })), runtimeDigest);
            snapshot = new(binding, quote, context); error = ""; return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    private static bool MatchesSelectedProfile(XElement root, XElement settings, ICharacterSourceDataContext context)
        => CharacterCareerSettingsProfileBinding.MatchesSelectedProfile(Scalar(root, "settings"), settings, context, "focus_bonding");

    // Source authorities use sha256:<hex>; transaction/receipt digests remain bare hex.
    // Preserve the original authority strings in the binding and currentness comparison.
    private static bool IsSourceDigest(string? value) => IsDigest(value)
        || value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) && IsDigest(value[7..]);

    public static bool IsSourceCurrent(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerFocusBondingRequest request, CharacterCareerFocusBondingSnapshot snapshot)
    {
        try
        {
            return TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _)
                && TryRead(owner, saved, request, sources, snapshot.Binding.RuntimeDigest, out var fresh, out _)
                && retained!.Binding == snapshot.Binding && fresh!.Binding == snapshot.Binding
                && Same(retained.Quote, snapshot.Quote) && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static IReadOnlyList<CharacterCareerFocusBondingEntry> ReadEntries(string xml)
    {
        var root = Parse(xml, "character"); Need(Flag(root, "created"), "focus_bonding_career_only");
        var inventory = ReadInventory(root);
        return inventory.Gear.Where(x => Scalar(x, "category", "") is "Foci" or "Metamagic Foci" or "Stacked Focus")
            .Select(x => new CharacterCareerFocusBondingEntry(Identity(x), Scalar(x, "name"), Integer(x, "rating", 0), Flag(x, "bonded"),
                x.Parent != Node(root, "gears") ? "Nested focus ownership is not supported by this operation."
                : Scalar(x, "category") == "Stacked Focus" ? "Stacked foci require a separate operation."
                : Scalar(x, "name").Contains("Individualized", StringComparison.Ordinal) ? "Individualized foci require a separate operation."
                : !SupportedName(Scalar(x, "name")) ? "This focus type requires an unsupported effect or selection."
                : null)).ToArray();
    }

    public static Guid BindingId(CharacterCareerFocusBondingRequest request)
        => new(Hash(request.WorkspaceId.Value + "\0" + request.OperationId.ToString("D") + "\0focus-binding")[..32]);

    private static string Apply(string content, CharacterCareerFocusBondingRequest request, CharacterCareerFocusBondingQuote quote)
    {
        if (!quote.ChangesState) return content;
        var root = Parse(content, "character");
        var gear = Box(root, "gears").Elements("gear").Single(x => Identity(x) == request.GearId);
        if (request.Bonded)
        {
            // Only the exact source-checked builtin grammar can reach this serializer.
            Box(root, "improvements").Add(BuiltinEffects(gear));
            var foci = Node(root, "foci"); if (foci is null) { foci = new("foci"); root.Add(foci); }
            Guid focusId = BindingId(request);
            foci.Add(new XElement("focus", new XElement("guid", focusId.ToString("D")), new XElement("gearid", request.GearId.ToString("D"))));
            Set(root, "karma", quote.KarmaAfter.ToString(CultureInfo.InvariantCulture));
            Box(root, "expenses").Add(new XElement("expense", new XElement("guid", ExpenseId(request).ToString("D")),
                new XElement("date", request.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)),
                new XElement("amount", (-quote.KarmaCost).ToString(CultureInfo.InvariantCulture)),
                new XElement("reason", "Bound " + quote.State.Name), new XElement("type", "Karma"),
                new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
                new XElement("undo", new XElement("karmatype", "BindFocus"), new XElement("nuyentype", "AddCyberware"),
                    new XElement("objectid", focusId.ToString("D")), new XElement("qty", "0"), new XElement("extra"))));
        }
        else
        {
            OwnedEffects(root, request.GearId).Remove();
            Box(root, "foci").Elements("focus").Single(x => Identity(x) == quote.State.FocusId).Remove();
        }
        Set(gear, "bonded", request.Bonded ? "True" : "False");
        // Neither branch equips/deletes the item. Unbond does not refund or add an expense.
        return root.Document!.ToString(SaveOptions.DisableFormatting);
    }

    private static Guid GuidValue(XElement node, string name)
    {
        Need(Guid.TryParseExact(Scalar(node, name), "D", out var id) && id != Guid.Empty, "focus_bonding_identity_invalid"); return id;
    }
    private static Guid Identity(XElement node) => GuidValue(node, "guid");
    private static XElement Parse(string xml, string name)
    {
        Need(xml.Length is > 0 and <= 67_108_864, "focus_bonding_xml_size_invalid");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Need(doc.Root is { } root && root.Name == name && !root.HasAttributes, "focus_bonding_xml_invalid");
        return doc.Root!;
    }
    private static XElement? Node(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(x => x.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes), "focus_bonding_ambiguous_field");
        return nodes.SingleOrDefault();
    }
    private static XElement Box(XElement parent, string name)
    {
        var node = Node(parent, name); Need(node is not null && !node.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value)), "focus_bonding_missing_container_" + name);
        return node!;
    }
    private static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var node = Node(parent, name); Need(node is not null || fallback is not null, "focus_bonding_missing_field_" + name);
        Need(node is null || !node.HasElements, "focus_bonding_scalar_invalid"); return node?.Value ?? fallback!;
    }
    private static int Integer(XElement parent, string name, int? fallback = null)
    {
        Need(int.TryParse(Scalar(parent, name, fallback?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value), "focus_bonding_integer_invalid"); return value;
    }
    private static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Scalar(parent, name, fallback?.ToString());
        if (bool.TryParse(value, out bool flag)) return flag;
        throw new InvalidDataException("focus_bonding_boolean_invalid");
    }
    private static bool Enabled(XElement improvement)
    {
        string value = Scalar(improvement, "enabled", "True");
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return number > 0;
        if (bool.TryParse(value, out bool enabled)) return enabled;
        throw new InvalidDataException("focus_bonding_effect_enabled_invalid");
    }
    private static void Set(XElement parent, string name, string value)
    { var node = Node(parent, name); if (node is null) parent.Add(new XElement(name, value)); else node.Value = value; }
    private static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
