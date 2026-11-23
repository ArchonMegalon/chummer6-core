using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerContactsTransaction
{
    private static readonly string[] IdentityNames = ["name", "role", "location", "notes", "customname", "metatype",
        "gender", "age", "contacttype", "preferredpayment", "hobbiesvice", "personallife", "groupname"];
    private static readonly HashSet<string> RemovableFields = new(IdentityNames.Concat(new[]
        { "guid", "type", "connection", "loyalty", "group", "free", "family", "blackmail", "file", "relative",
          "groupenabled", "mademan", "notesColor", "colour", "sex", "mainmugshotindex", "mugshots" }), StringComparer.Ordinal);

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerContactChange change,
        ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerContactsSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "contact_workspace_invalid";
        if (!IsValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || !CharacterCareerContactsRules.IsValidChange(change) || !IsDigest(runtimeDigest)) return false;
        var document = saved.Document;
        error = "contact_requires_saved_sr5_career";
        if (document.Format != WorkspaceDocumentFormat.NativeXml || document.RulesetId != "sr5"
            || document.SchemaVersion != 1 || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            XElement root = ReadXml(document.Content).Root!;
            if (!Boolean(root, "created", required: true)) return false;
            var contacts = Container(root, "contacts");
            var rows = contacts?.Elements("contact").ToArray() ?? [];
            error = "contact_collection_limit";
            if (rows.Length >= 100_000) return false;
            // Unrelated legacy contacts without stable IDs are retained, not silently repaired.
            var targets = rows.Where(row => Guid.TryParse(Scalar(row, "guid"), out Guid id) && id == change.ContactId).ToArray();
            error = "contact_target_identity_ambiguous";
            if (targets.Length > 1) return false;
            XElement? target = targets.SingleOrDefault();
            bool adding = change.Kind == CharacterCareerContactChangeKind.Add;
            error = adding ? "contact_duplicate_identity" : "contact_target_not_found";
            if (adding != (target is null)) return false;
            error = "contact_identity_previously_used";
            if (adding && document.AuxiliaryState.CharacterCareerContactsReceipts?.Any(row =>
                row.Command.Request.Change.ContactId == change.ContactId) == true) return false;
            bool referenced = ReferencesContact(root, target, document.AuxiliaryState, change.ContactId);
            error = "contact_identity_reserved_by_relation";
            if (adding && referenced) return false;
            if (!TryReadEffects(root, change.ContactId, out var effects, out error)) return false;
            CharacterCareerContactState? before = null;
            if (target is not null)
            {
                error = "contact_target_shape_invalid";
                if (target.HasAttributes) return false;
                string entity = Scalar(target, "type");
                error = "contact_entity_type_unsupported";
                CharacterCareerContactEntityType type;
                if (entity is "" or "Contact") type = CharacterCareerContactEntityType.Contact;
                else if (entity == "Enemy") type = CharacterCareerContactEntityType.Enemy;
                else if (entity == "Pet") { error = "contact_pet_requires_separate_authority"; return false; }
                else return false;
                bool linked = !string.IsNullOrWhiteSpace(Scalar(target, "file")) || !string.IsNullOrWhiteSpace(Scalar(target, "relative"));
                string? removalBlocker = linked ? "contact_linked_relation" : referenced ? "contact_owned_relation" : null;
                // Unknown/nested owned data must be explicitly cleaned up; editing known scalars preserves it.
                if (removalBlocker is null && target.Elements().Any(node => !RemovableFields.Contains(node.Name.LocalName)
                    || node.Name.Namespace != XNamespace.None || node.HasAttributes || node.HasElements
                    || (node.Name == "mugshots" && !string.IsNullOrWhiteSpace(node.Value))))
                    removalBlocker = "contact_nested_state_requires_cleanup";
                var identity = new CharacterCareerContactIdentity(Scalar(target, "name"), Scalar(target, "role"),
                    Scalar(target, "location"), Scalar(target, "notes"), Scalar(target, "customname"), Scalar(target, "metatype"),
                    target.Element("gender") is null ? Scalar(target, "sex") : Scalar(target, "gender"), Scalar(target, "age"),
                    Scalar(target, "contacttype"), Scalar(target, "preferredpayment"), Scalar(target, "hobbiesvice"),
                    Scalar(target, "personallife"), Scalar(target, "groupname"));
                error = "contact_target_values_invalid";
                if (!CharacterCareerContactsRules.IsValidIdentity(identity)) return false;
                before = new(change.ContactId, identity, type, Integer(target, "connection", 1), Integer(target, "loyalty", 1),
                    Boolean(target, "group"), Boolean(target, "free"), Boolean(target, "family"), Boolean(target, "blackmail"),
                    Container(target, "readonly") is not null, linked, removalBlocker);
                if (before.Connection is < 1 or > 12 || before.Loyalty is < 1 or > 6) return false;
            }
            error = "contact_source_unavailable";
            var context = sources.TryCreateContext(document.Content);
            if (!TryReadRuleState(context, out string rules)) return false;
            string targetDigest = Hash(target is null ? "chummer.core.contact-absent/v1\0" + change.ContactId.ToString("D")
                : target.ToString(SaveOptions.DisableFormatting));
            var binding = new CharacterCareerContactsBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                SourceDigest(document), document.AuxiliaryStateDigest, rules, runtimeDigest, targetDigest);
            error = "contact_source_changed";
            if (!TryReadRuleState(context, out string finalRules) || rules != finalRules) return false;
            snapshot = new(binding, before, Array.AsReadOnly(effects));
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "contact_input_unresolved"; return false; }
    }

    private static bool ReferencesContact(XElement root, XElement? target, WorkspaceDocumentAuxiliaryState auxiliary, Guid id)
    {
        // A historical purchase/creation selection is not a current ownership relation.
        // Only source grant acceptances supplement the live XML relationships here.
        var ownership = new
        {
            auxiliary.LifeModuleDecisionAcceptances,
            ArchivedLifeModuleAcceptances = auxiliary.CharacterCreationFinalizationArchive?.State.LifeModuleDecisionAcceptances
        };
        bool Ref(string value) => Guid.TryParse(value.Trim(), out Guid candidate) && candidate == id;
        return root.Descendants().Any(node => !node.HasElements && !node.AncestorsAndSelf().Contains(target)
                && Ref(node.Value))
            || root.Descendants().Where(node => !node.AncestorsAndSelf().Contains(target)).Attributes().Any(attribute => Ref(attribute.Value))
            || JsonSerializer.Serialize(ownership).Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadEffects(XElement root, Guid id, out CharacterContactRuleEffect[] effects, out string error)
    {
        effects = [];
        error = "contact_effect_shape_invalid";
        var result = new List<CharacterContactRuleEffect>();
        foreach (var row in Container(root, "improvements")?.Elements("improvement") ?? [])
        {
            string type = Scalar(row, "improvementttype");
            if (!type.Contains("Contact", StringComparison.OrdinalIgnoreCase) && type != "FriendsInHighPlaces") continue;
            string name = Scalar(row, "improvedname");
            if (name.Length > 0 && (!Guid.TryParse(name, out Guid effectId) || effectId != id)) continue;
            int enabled = Integer(row, "enabled", 1);
            string condition = Scalar(row, "condition");
            if (enabled <= 0 || condition == "create") continue;
            error = "contact_effect_condition_unsupported";
            if (condition is not ("" or "career")) return false;
            // Creation point modifiers have no monetary or Karma effect on a manual Career edit.
            if (type is "ContactKarmaDiscount" or "ContactKarmaMinimum" or "AddContact") continue;
            error = "contact_effect_type_unsupported";
            if (type is not ("ContactForceGroup" or "ContactMakeFree" or "ContactForcedLoyalty" or "FriendsInHighPlaces")) return false;
            error = "contact_effect_target_unresolved";
            if (type != "FriendsInHighPlaces" && name.Length == 0) return false;
            result.Add(new(type, name.Length == 0 ? "" : id.ToString("D"), Scalar(row, "val"), Scalar(row, "unique"),
                Boolean(row, "custom"), enabled, condition));
            error = "contact_effect_limit";
            if (result.Count > 1024) return false;
        }
        effects = result.ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryApply(string content, CharacterCareerContactChange change, CharacterCareerContactsQuote quote, out string payload)
    {
        payload = string.Empty;
        XDocument document = ReadXml(content);
        XElement root = document.Root!;
        var contacts = Container(root, "contacts");
        XElement? target = contacts?.Elements("contact").SingleOrDefault(row => Guid.TryParse(Scalar(row, "guid"), out Guid id) && id == change.ContactId);
        if (change.Kind == CharacterCareerContactChangeKind.Remove)
        {
            if (target is null || quote.After is not null) return false;
            target.Remove();
        }
        else
        {
            var after = quote.After!;
            if (change.Kind == CharacterCareerContactChangeKind.Add)
            {
                if (target is not null) return false;
                target = new XElement("contact", new XElement("guid", change.ContactId.ToString("D")),
                    new XElement("type", after.EntityType.ToString()));
                if (contacts is null) { contacts = new XElement("contacts"); root.Add(contacts); }
                contacts.Add(target);
            }
            if (target is null) return false;
            var before = quote.Before;
            if (before is null || before.Identity != after.Identity)
            {
                string[] values = IdentityValues(after.Identity);
                string[]? old = before is null ? null : IdentityValues(before.Identity);
                for (int i = 0; i < IdentityNames.Length; i++)
                    if (old is null || values[i] != old[i]) SetScalar(target, IdentityNames[i], values[i]);
            }
            if (before is null || before.Connection != after.Connection) SetScalar(target, "connection", after.Connection.ToString(CultureInfo.InvariantCulture));
            if (before is null || before.Loyalty != after.Loyalty) SetScalar(target, "loyalty", after.Loyalty.ToString(CultureInfo.InvariantCulture));
            if (before is null || before.IsGroup != after.IsGroup) SetScalar(target, "group", after.IsGroup.ToString());
            if (before is null || before.Free != after.Free) SetScalar(target, "free", after.Free.ToString());
            if (before is null || before.Family != after.Family) SetScalar(target, "family", after.Family.ToString());
            if (before is null || before.Blackmail != after.Blackmail) SetScalar(target, "blackmail", after.Blackmail.ToString());
        }
        payload = document.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static string[] IdentityValues(CharacterCareerContactIdentity identity) => [identity.Name, identity.Role,
        identity.Location, identity.Notes, identity.CustomName, identity.Metatype, identity.Gender, identity.Age,
        identity.ContactType, identity.PreferredPayment, identity.HobbiesVice, identity.PersonalLife, identity.GroupName];

    private static void SetScalar(XElement parent, string name, string value)
    {
        var node = Container(parent, name);
        if (node is null) parent.Add(new XElement(name, value));
        else { if (node.HasElements) throw new InvalidDataException(); node.Value = value; }
    }

    private static XDocument ReadXml(string content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > MaximumContentLength) throw new InvalidDataException();
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "character" || document.Root.HasAttributes) throw new InvalidDataException();
        return document;
    }

    private static XElement? Container(XElement parent, string name)
    {
        var nodes = parent.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1 || nodes.Any(node => node.HasAttributes)) throw new InvalidDataException();
        return nodes.SingleOrDefault();
    }

    private static string Scalar(XElement parent, string name)
    {
        var node = Container(parent, name);
        if (node?.HasElements == true) throw new InvalidDataException();
        return node?.Value ?? string.Empty;
    }

    private static int Integer(XElement parent, string name, int fallback)
        => parent.Element(name) is null ? fallback : int.Parse(Scalar(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static bool Boolean(XElement parent, string name, bool required = false)
    {
        if (!required && parent.Element(name) is null) return false;
        string value = Scalar(parent, name);
        return value switch { "1" => true, "0" => false, _ => bool.Parse(value) };
    }
}
