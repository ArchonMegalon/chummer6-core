using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerFreeSpriteConversionTransaction
{
    public static bool TryRead(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerFreeSpriteConversionRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerFreeSpriteConversionSnapshot? snapshot, out string error)
        => TryReadCurrent5732(owner, saved, request, sources, runtimeDigest, null, out snapshot, out error);

    internal static bool TryReadCurrent5732(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerFreeSpriteConversionRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        CharacterCurrentRuleEnvironmentContext? current,
        out CharacterCareerFreeSpriteConversionSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "free_sprite_conversion_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, current, out snapshot, out error);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool TryReadCore(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerFreeSpriteConversionRequest request, ICharacterSourceDataContext context, string runtimeDigest,
        CharacterCurrentRuleEnvironmentContext? current,
        out CharacterCareerFreeSpriteConversionSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "free_sprite_conversion_state_invalid";
        string phase = "workspace";
        try
        {
            Need(owner.IsValid && IsValidRequest(request) && request.WorkspaceId == saved.Id
                && request.ExpectedWorkspaceRevision == saved.ContentRevision && saved.SavedRevision == saved.ContentRevision
                && IsValidHistory(saved) && CharacterCareerPowerGovernance.Allows(saved) && IsDigest(runtimeDigest),
                "free_sprite_conversion_workspace_unavailable");
            if (current is not null)
            {
                phase = "retained_sheet";
                Need(ReferenceEquals(current.Current, saved) && current.IsUnchanged && current.VerifyRetainedArchive()
                    && CharacterSheetDerivedQuery.ParseImportedCareer5688(current).IsCurrent,
                    "free_sprite_conversion_retained_sheet_unsupported");
            }
            phase = "character";
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created"), "free_sprite_conversion_career_only");
            phase = "profile";
            Need(context.TryResolveCareerSettingsProfile(out string profile, out string authority)
                && !string.IsNullOrWhiteSpace(authority), "free_sprite_conversion_profile_unavailable");
            Need(Scalar(root, "settings") == Scalar(Parse(profile, "settings"), "id"), "free_sprite_conversion_profile_binding_invalid");
            phase = "metatype";
            Need(CharacterSheetDerivedQuery.TryMetatype(root, context, out string metatypeXml, out string metatypeState, out _),
                "free_sprite_conversion_metatype_source_unavailable");
            string category = Scalar(root, "metatypecategory");
            Need(Scalar(Parse(metatypeXml, "metatype"), "category") == category,
                "free_sprite_conversion_category_source_mismatch");
            phase = "conversion";
            var powers = Box(root, "critterpowers");
            Need(powers.Elements().All(p => p.Name == "critterpower" && !p.HasAttributes), "free_sprite_conversion_powers_invalid");
            var ids = powers.Elements().Select(Identity).ToArray();
            Need(CharacterFreeSpriteConversionRules.TryCreateState(true, category, ids, out var state)
                && CharacterFreeSpriteConversionRules.TryCreateIdentity(state, request.CritterPowerId, out _),
                "free_sprite_conversion_not_eligible");
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id) && id == request.CritterPowerId),
                "free_sprite_conversion_instance_collision");
            CharacterFreeSpriteConversionRules.TryCreateIdentity(state, request.CritterPowerId, out var identity);
            Need(CharacterFreeSpriteConversionRules.TryValidateMutation(state, identity, state.Revision), "free_sprite_conversion_invalid_transition");
            // This compiler resolves the exact enabled/amended Denial source and compiles its
            // effects with the existing Core owner dispatcher. No hardcoded catalog row.
            phase = "denial";
            CharacterSheetOwnerGraph.CreateFreeSpriteDenial5732(root, context, identity.CritterPowerId,
                out var power, out var effects, out var source);
            phase = "economics";
            int karma = Integer(root, "karma");
            Need(decimal.TryParse(Scalar(root, "nuyen"), NumberStyles.Number, CultureInfo.InvariantCulture, out var nuyen),
                "free_sprite_conversion_nuyen_invalid");
            phase = "quote";
            var quote = new CharacterCareerFreeSpriteConversionQuote(state, identity,
                CharacterFreeSpriteConversionRules.FreeSpriteCategory, source.Name, source.SourceId, source.SourceBook,
                karma, karma, nuyen, nuyen, Hash(JsonSerializer.Serialize(source)),
                power.ToString(SaveOptions.DisableFormatting), effects.Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray());
            Need(IsValidQuote(request, quote), "free_sprite_conversion_quote_invalid");
            phase = "binding";
            var binding = new CharacterCareerFreeSpriteConversionBinding(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser,
                owner.AuthorityInstanceId, owner.TransitionRevision, saved.Id, saved.ContentRevision, saved.LocalHistory!.IncarnationId,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest,
                Hash(JsonSerializer.Serialize(new { profile, authority, metatypeXml, metatypeState, source,
                    Semantics = "sr5-free-sprite-conversion/v1", Retained = current?.Capture.IntegrityDigest })), runtimeDigest);
            snapshot = new(binding, quote, context) { Current5732 = current }; error = ""; return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e))
        {
            // Fixed phase/type codes only: never expose exception messages, XML, values or paths.
            string kind = e switch
            {
                XmlException => "xml",
                FormatException => "format",
                OverflowException => "overflow",
                ArgumentException => "argument",
                InvalidOperationException => "invalid_operation",
                NullReferenceException => "null_reference",
                _ => "malformed"
            };
            error = "free_sprite_conversion_state_invalid:" + phase + ":" + kind;
            return false;
        }
    }
    public static bool IsSourceCurrent(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerFreeSpriteConversionRequest request, CharacterCareerFreeSpriteConversionSnapshot snapshot)
    {
        try
        {
            return TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, snapshot.Current5732, out var retained, out _)
                && TryReadCurrent5732(owner, saved, request, sources, snapshot.Binding.RuntimeDigest, snapshot.Current5732, out var fresh, out _)
                && retained!.Binding == snapshot.Binding && fresh!.Binding == snapshot.Binding
                && Same(retained.Quote, snapshot.Quote) && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, snapshot.Current5732, out retained, out _)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static string Apply(string content, CharacterCareerFreeSpriteConversionRequest request, CharacterCareerFreeSpriteConversionQuote quote)
    {
        var root = Parse(content, "character");
        var powers = Box(root, "critterpowers");
        Need(CharacterFreeSpriteConversionRules.TryCreateState(Flag(root, "created"), Scalar(root, "metatypecategory"),
            powers.Elements().Select(Identity).ToArray(), out var state)
            && CharacterFreeSpriteConversionRules.TryValidateMutation(state, quote.Identity, quote.State.Revision),
            "free_sprite_conversion_state_changed");
        powers.Add(Parse(quote.DenialPowerXml, "critterpower"));
        var improvements = Box(root, "improvements");
        foreach (var effect in quote.DenialEffectsXml) improvements.Add(Parse(effect, "improvement"));
        Set(root, "metatypecategory", quote.CategoryAfter);
        // Preserve balances, expenses, complete unknown XML, linked files, and outer envelope.
        return CharacterCareerLearningDocument5703.PreserveEnvelope(content, root.ToString(SaveOptions.DisableFormatting));
    }
    private static Guid Identity(XElement node)
    {
        Need(Guid.TryParseExact(Scalar(node, "guid"), "D", out var id) && id != Guid.Empty, "free_sprite_conversion_identity_invalid");
        return id;
    }
    private static XElement Parse(string xml, string name)
    {
        Need(xml.Length is > 0 and <= 67_108_864, "free_sprite_conversion_xml_size_invalid");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Need(doc.Root is { } root && root.Name == name && !root.HasAttributes, "free_sprite_conversion_xml_invalid");
        return doc.Root!;
    }
    private static XElement? Node(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(x => x.Name.LocalName == name).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || nodes[0].Name == name && !nodes[0].HasAttributes), "free_sprite_conversion_ambiguous_field");
        return nodes.SingleOrDefault();
    }
    private static XElement Box(XElement parent, string name)
    {
        var node = Node(parent, name); Need(node is not null && !node.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value)), "free_sprite_conversion_missing_container_" + name);
        return node!;
    }
    private static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var node = Node(parent, name); Need(node is not null || fallback is not null, "free_sprite_conversion_missing_field_" + name);
        Need(node is null || !node.HasElements, "free_sprite_conversion_scalar_invalid"); return node?.Value.Trim() ?? fallback!;
    }
    private static int Integer(XElement parent, string name, int? fallback = null)
    {
        Need(int.TryParse(Scalar(parent, name, fallback?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value), "free_sprite_conversion_integer_invalid"); return value;
    }
    private static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Scalar(parent, name, fallback?.ToString());
        if (bool.TryParse(value, out bool flag)) return flag;
        throw new InvalidDataException("free_sprite_conversion_boolean_invalid");
    }
    private static bool Enabled(XElement improvement)
    {
        string value = Scalar(improvement, "enabled", "True");
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return number > 0;
        if (bool.TryParse(value, out bool enabled)) return enabled;
        throw new InvalidDataException("free_sprite_conversion_effect_enabled_invalid");
    }
    private static void Set(XElement parent, string name, string value)
    { var node = Node(parent, name); if (node is null) parent.Add(new XElement(name, value)); else node.Value = value; }
    private static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
