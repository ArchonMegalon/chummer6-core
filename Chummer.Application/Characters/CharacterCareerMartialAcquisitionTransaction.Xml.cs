using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerMartialAcquisitionTransaction
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static bool TryRead(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerMartialAcquisitionRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerMartialAcquisitionSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "martial_acquisition_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            return context is not null && TryReadCore(owner, saved, request, context, runtimeDigest, out snapshot, out error);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool TryReadCore(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerMartialAcquisitionRequest request, ICharacterSourceDataContext context, string runtimeDigest,
        out CharacterCareerMartialAcquisitionSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "martial_acquisition_state_invalid";
        try
        {
            Need(owner.IsValid && IsValidRequest(request) && request.WorkspaceId == saved.Id
                && saved.ContentRevision is > 0 and < long.MaxValue && saved.SavedRevision == saved.ContentRevision
                && IsValidHistory(saved) && CharacterCareerPowerGovernance.Allows(saved) && IsDigest(runtimeDigest),
                "martial_acquisition_workspace_unavailable");
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created") && !Flag(root, "ignorerules", false), "martial_acquisition_requires_rules_career");
            Need(context.TryResolveCareerSettingsProfile(out string profile, out string authority)
                && !string.IsNullOrWhiteSpace(authority), "martial_acquisition_profile_unavailable");
            var settings = Parse(profile, "settings");
            Need(Scalar(root, "settings") == Scalar(settings, "id"), "martial_acquisition_profile_binding_invalid");
            if (request.CatalogBinding is { } selected)
                Need(CatalogBinding(owner, saved, context, runtimeDigest, out _) == selected,
                    "martial_acquisition_catalog_changed");
            Need(context is ICharacterCareerMartialAcquisitionSourceContext, "martial_acquisition_source_capability_unavailable");
            var resolver = (ICharacterCareerMartialAcquisitionSourceContext)context;
            Need(resolver.TryResolveCareerMartialAcquisitionSource(request.SourceId, request.Kind, out var evidence)
                && evidence is not null && !string.IsNullOrWhiteSpace(evidence.AuthorityState), "martial_acquisition_source_unavailable");
            bool art = request.Kind == CharacterCareerMartialAcquisitionKind.Art;
            var source = Parse(evidence!.Xml, art ? "martialart" : "technique");
            ValidateMartialSource(source, request.SourceId, art);
            string requirementsAuthority = CheckRequirements(source, root, settings, context);
            var arts = MartialArts(root);
            XElement? parent = null;
            string parentXml = "", parentAuthority = "", parentName = "", legacyAuthority = "";
            XElement[] owned;
            bool first = false;
            int rate = TechniqueRate(settings), cost;
            if (art)
            {
                owned = arts;
                cost = Node(source, "cost") is null ? 7 : Integer(source, "cost");
                Need(cost >= 0, "martial_acquisition_negative_art_cost_unsupported");
            }
            else
            {
                parent = arts.SingleOrDefault(x => GuidValue(x, "guid") == request.ParentArtId);
                Need(parent is not null, "martial_acquisition_parent_missing");
                Guid parentSourceId = GuidValue(parent!, "sourceid");
                Need(resolver.TryResolveCareerMartialAcquisitionSource(parentSourceId, CharacterCareerMartialAcquisitionKind.Art,
                    out var parentEvidence) && parentEvidence is not null && !string.IsNullOrWhiteSpace(parentEvidence.AuthorityState),
                    "martial_acquisition_parent_source_unavailable");
                parentXml = parentEvidence!.Xml; parentAuthority = parentEvidence.AuthorityState;
                var parentSource = Parse(parentXml, "martialart");
                ValidateMartialSource(parentSource, parentSourceId, true, ownedParent: true);
                parentName = Scalar(parentSource, "name");
                Need(parentName == Scalar(parent!, "name") && Scalar(parentSource, "source") == Scalar(parent!, "source")
                    && Scalar(parentSource, "page") == Scalar(parent!, "page")
                    && Flag(parent!, "isquality", false) == Flag(parentSource, "isquality", false),
                    "martial_acquisition_parent_source_mismatch");
                owned = Techniques(parent!, parentSource, resolver, out legacyAuthority);
                first = owned.Length == 0;
                Need(AllowsTechnique(parentSource, Scalar(source, "name"), first), "martial_acquisition_technique_not_in_art");
                cost = first ? 0 : rate;
            }
            // SelectionShared default one, with techniques scoped to the selected parent art.
            Need(!owned.Any(x => GuidValue(x, "sourceid") == request.SourceId || Scalar(x, "name") == Scalar(source, "name")),
                "martial_acquisition_already_owned");
            int karma = Integer(root, "karma");
            Need(karma >= cost, "martial_acquisition_insufficient_karma");
            var effects = MartialEffects(root, source, settings, context, request, out string effectsAuthority);
            Need(Box(root, "expenses").Elements().All(x => x.Name == "expense")
                && Box(root, "improvements").Elements().All(x => x.Name == "improvement"), "martial_acquisition_history_invalid");
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id)
                && (id == ExpenseId(request) || id == InstanceId(request, "item"))), "martial_acquisition_identity_conflict");
            string rules = Hash(JsonSerializer.Serialize(new { authority, evidence.AuthorityState, parentAuthority,
                requirementsAuthority, effectsAuthority, legacyAuthority, Semantics = "sr5-career-martial-acquisition/v4-first-only-grant" }));
            var binding = new CharacterCareerMartialAcquisitionBinding(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser,
                owner.AuthorityInstanceId, owner.TransitionRevision, saved.Id, saved.ContentRevision, saved.LocalHistory!.IncarnationId,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest, rules, runtimeDigest);
            var quote = new CharacterCareerMartialAcquisitionQuote(request.SourceId, request.Kind, request.ParentArtId,
                Scalar(source, "name"), parentName, Scalar(source, "source"), Scalar(source, "page"), cost, karma,
                checked(karma - cost), rate, first, Flag(settings, "freemartialartspecialization", false), evidence.Xml, parentXml, effects);
            Need(IsValidQuote(request, quote), "martial_acquisition_quote_invalid");
            snapshot = new(binding, quote, context); error = ""; return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    public static bool IsSourceCurrent(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerMartialAcquisitionRequest request, CharacterCareerMartialAcquisitionSnapshot snapshot)
    {
        try
        {
            return TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _)
                && retained!.Binding == snapshot.Binding && Same(retained.Quote, snapshot.Quote)
                && TryRead(owner, saved, request, sources, snapshot.Binding.RuntimeDigest, out var fresh, out _)
                && fresh!.Binding == snapshot.Binding && Same(fresh.Quote, snapshot.Quote)
                && TryReadCore(owner, saved, request, snapshot.Context, snapshot.Binding.RuntimeDigest, out var last, out _)
                && last!.Binding == snapshot.Binding && Same(last.Quote, snapshot.Quote);
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
    // Pure consistency check only. A matching projection does NOT prove execution,
    // command provenance or observed after-state. Portable recovery must verify the
    // app-owned authenticated terminal and ancestry; this helper grants no authority.
    public static bool MatchesRestoredReceiptTransition(WorkspaceDocument before, CharacterCareerMartialAcquisitionReceipt receipt)
    {
        try
        {
            return IsCoherent(receipt) && !receipt.NeverApplied && receipt.Quote is { } quote
                && SourceDigest(before) == receipt.Command.Binding.SourceDigest
                && before.AuxiliaryStateDigest == receipt.Command.Binding.AuxiliaryStateDigest
                && Hash(Apply(before.Content, receipt.Command.Request, quote)) == receipt.CharacterPayloadDigestAfter;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    private static string Apply(string content, CharacterCareerMartialAcquisitionRequest request, CharacterCareerMartialAcquisitionQuote quote)
    {
        var root = Parse(content, "character");
        Need(Integer(root, "karma") == quote.KarmaBefore, "martial_acquisition_karma_conflict");
        bool art = request.Kind == CharacterCareerMartialAcquisitionKind.Art;
        var source = Parse(quote.SourceXml, art ? "martialart" : "technique");
        var item = new XElement(art ? "martialart" : "martialarttechnique",
            new XElement("name", quote.Name), new XElement("sourceid", request.SourceId.ToString("D")),
            new XElement("guid", InstanceId(request, "item").ToString("D")), new XElement("source", quote.Book), new XElement("page", quote.Page));
        if (art) item.Add(new XElement("cost", quote.KarmaCost), new XElement("isquality", "False"), new XElement("martialarttechniques"));
        item.Add(new XElement("notes", Scalar(source, "altnotes", Scalar(source, "notes", ""))),
            new XElement("notesColor", Scalar(source, "notesColor", "#000000")));
        if (art) AppendNode(root, "martialarts", item);
        else AppendNode(MartialArts(root).Single(x => GuidValue(x, "guid") == request.ParentArtId), "martialarttechniques", item);
        ApplyEffects(root, request, quote.Effects);
        root.SetElementValue("karma", quote.KarmaAfter.ToString(Invariant));
        var expense = new XElement("expense", new XElement("guid", ExpenseId(request).ToString("D")),
            new XElement("date", request.ExpenseDateLocal.ToString("s", Invariant)), new XElement("amount", (-quote.KarmaCost).ToString(Invariant)),
            new XElement("reason", (art ? "Learned Martial Art " : "Learned Technique ") + quote.Name), new XElement("type", "Karma"),
            new XElement("refund", "False"), new XElement("forcecareervisible", "False"), new XElement("undo",
                new XElement("karmatype", art ? "AddMartialArt" : "AddMartialArtTechnique"), new XElement("nuyentype", "AddCyberware"),
                new XElement("objectid", InstanceId(request, "item").ToString("D")), new XElement("qty", 0), new XElement("extra", "")));
        // Same chronological ordering as ExpenseEntries.AddWithSort; preserve existing rows verbatim.
        var expenses = Box(root, "expenses"); if (expenses.Parent is null) root.Add(expenses);
        var later = expenses.Elements("expense").FirstOrDefault(x => DateTime.TryParse(Scalar(x, "date"), Invariant,
            DateTimeStyles.None, out var date) && date > request.ExpenseDateLocal);
        if (later is null) expenses.Add(expense); else later.AddBeforeSelf(expense);
        string payload = root.ToString(SaveOptions.DisableFormatting);
        Need(payload.Length <= 67_108_864, "martial_acquisition_payload_too_large"); return payload;
    }
    private static void AppendNode(XElement root, string name, XElement row)
    { var box = Box(root, name); if (box.Parent is null) root.Add(box); box.Add(row); }
    internal static XElement Parse(string xml, string name)
    {
        Need(xml.Length <= 67_108_864, "martial_acquisition_xml_too_large");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
        Need(root.Name == name && !root.HasAttributes, "martial_acquisition_xml_root_invalid"); return root;
    }
    internal static XElement? Node(XElement root, string name)
    {
        var rows = root.Elements().Where(x => x.Name.LocalName == name).Take(2).ToArray();
        Need(rows.Length <= 1 && (rows.Length == 0 || rows[0].Name == name && !rows[0].HasAttributes),
            "martial_acquisition_xml_field_invalid"); return rows.SingleOrDefault();
    }
    internal static XElement Box(XElement root, string name) => Node(root, name) ?? new XElement(name);
    internal static string Scalar(XElement root, string name, string? fallback = null)
    { var node = Node(root, name); Need(node is not null || fallback is not null, "martial_acquisition_field_missing");
      Need(node is null || !node.HasElements, "martial_acquisition_scalar_invalid"); return node?.Value ?? fallback!; }
    internal static int Integer(XElement root, string name) => int.Parse(Scalar(root, name), NumberStyles.Integer, Invariant);
    internal static Guid GuidValue(XElement root, string name)
    { Need(Guid.TryParse(Scalar(root, name), out var id) && id != Guid.Empty, "martial_acquisition_guid_invalid"); return id; }
    internal static bool Flag(XElement root, string name, bool? fallback = null)
    { string s = Scalar(root, name, fallback?.ToString()); Need(bool.TryParse(s, out bool value), "martial_acquisition_boolean_invalid"); return value; }
    internal static void Need(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
