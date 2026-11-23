using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;
using static Chummer.Application.Session.WorkspaceSessionPlayAuthorityResolver;

namespace Chummer.Application.Session;

internal static partial class SessionPlayWeaponAuthority
{
    // Physical round custody, not a damage/effect evaluator. Canonical category type and ammo
    // source fields authorize compatibility. Catalog prices, availability and qty diagnostics do not.
    private static readonly HashSet<string> RoundTypes = new(StringComparer.Ordinal)
        { "gun", "cannon", "bow", "crossbow", "taser", "glauncher", "mlauncher", "gyrojet", "supermach", "dartgun" };
    private sealed record Ammo(XElement Row, XElement Source, SessionPlayAmmoInventory Item);

    internal static IReadOnlyList<CharacterWeaponFireState> Resolve(XElement root, string xml,
        ICharacterSourceDataResolver resolver, List<SessionPlayDiagnostic> diagnostics, out SessionPlayAmmunition? ammunition)
        => ResolveCore3828(root, xml, resolver, diagnostics, out ammunition, null);

    // No caller-supplied boolean/digest can waive the modifier guard. This entry
    // independently proves the complete sheet against the exact current context.
    // It retains all improvements and computes only detached round custody.
    internal static IReadOnlyList<CharacterWeaponFireState> ResolveForPhysicalRoutine3828(XElement root, string xml,
        ICharacterSourceDataResolver resolver, List<SessionPlayDiagnostic> diagnostics, out SessionPlayAmmunition? ammunition,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
    {
        ammunition = null;
        if (!XNode.DeepEquals(root, Parse(xml))) return [];
        var sheet = CharacterSheetDerivedQuery.ParsePhysicalAmmunition5701(xml, resolver, diagnostics,
            out _, out var weapons, out var custody, priorityGrants);
        if (sheet.UnavailableReason is { } unavailable)
        {
            // Preserve only the engine's bounded reason code, never its owner
            // path, character names, saved values or a raw exception message.
            string code = unavailable.StartsWith("sheet:", StringComparison.Ordinal)
                ? unavailable.Split(':', 3).ElementAtOrDefault(1) ?? "" : "";
            if (code.Length is < 1 or > 96 || code.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
                code = "unavailable";
            if (code == "missing-field")
                code += "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(unavailable))).ToLowerInvariant();
            diagnostics.Add(new("physical-sheet-" + code, "ammo-reload",
                "The complete character calculation required for ammunition is unavailable."));
            return [];
        }
        ammunition = custody;
        return weapons;
    }

    // The owner graph must be the complete compiler for these exact inputs and
    // this exact context. A public sheet DTO/digest cannot waive the guard.
    internal static IReadOnlyList<CharacterWeaponFireState> ResolveForSheet5701(XElement root,
        ICharacterSourceDataContext context, CharacterSheetOwnerGraph owners,
        List<SessionPlayDiagnostic> diagnostics, out SessionPlayAmmunition? ammunition)
    {
        ammunition = null;
        if (!owners.MatchesAmmunitionContext5701(root, context)) return [];
        string xml = root.ToString(SaveOptions.DisableFormatting);
        var result = ResolveCore3828(root, xml, new PhysicalRoutineContext3828(xml, context), diagnostics, out var projected, context);
        if (!owners.MatchesAmmunitionContext5701(root, context)) return [];
        ammunition = projected;
        return result;
    }

    private sealed class PhysicalRoutineContext3828(string xml, ICharacterSourceDataContext context) : ICharacterSourceDataResolver
    { public ICharacterSourceDataContext? TryCreateContext(string characterXml) => characterXml == xml ? context : null; }

    private static IReadOnlyList<CharacterWeaponFireState> ResolveCore3828(XElement root, string xml,
        ICharacterSourceDataResolver resolver, List<SessionPlayDiagnostic> diagnostics, out SessionPlayAmmunition? ammunition,
        ICharacterSourceDataContext? provenSheetContext)
    {
        ammunition = null;
        var weapons = new List<CharacterWeaponFireState>();
        var custody = new List<SessionPlayAmmoWeapon>();
        try
        {
            var owned = One(root, "weapons")?.Elements("weapon").ToArray() ?? [];
            if (owned.Length == 0) return weapons;
            Require(owned.Length <= 64, "weapon-capacity-unavailable");
            var context = resolver.TryCreateContext(xml);
            Require(context is not null, "weapon-source-unavailable");
            Require(context!.TryResolveCreationLoadoutAuthority(out var authority)
                && CharacterCreationLoadoutRules.IsValidAuthority(authority), "weapon-source-unavailable");
            Require(Version.TryParse(Text(root, "appversion", ""), out var savedVersion)
                && savedVersion >= new Version(5, 222, 62), "legacy-clip-migration-required");
            // Duplicate IDs anywhere can alias vehicle/child/unsupported custody. Never guess ownership.
            var allGear = root.Descendants("gear").Where(g => One(g, "guid") is not null).ToArray();
            var allWeapons = root.Descendants("weapon").Where(w => One(w, "guid") is not null).ToArray();
            var allAttachments = root.Descendants("accessory").Where(a => One(a, "guid") is not null).ToArray();
            Require(allGear.Select(Id).Distinct().Count() == allGear.Length
                && allWeapons.Select(Id).Distinct().Count() == allWeapons.Length
                && allAttachments.Select(Id).Distinct().Count() == allAttachments.Length
                && allGear.Concat(allWeapons).Concat(allAttachments).Select(Id).Distinct().Count()
                    == allGear.Length + allWeapons.Length + allAttachments.Length, "ammunition-identity-ambiguous");
            Require(!allWeapons.Any(w => w.Elements().Any(e => e.Name.LocalName.StartsWith("ammoremaining", StringComparison.Ordinal)
                || e.Name.LocalName.StartsWith("ammoloaded", StringComparison.Ordinal))), "legacy-clip-migration-required");
            var catalog = authority.Options;
            var gearWitnesses = new Dictionary<Guid, CharacterCareerGearPurchaseSource>();
            var attachmentWitnesses = new Dictionary<Guid, CharacterSheetOwnerSource>();
            var ownerSources = context as ICharacterSheetOwnerSourceContext;
            CharacterSheetOwnerSource AttachmentSource(Guid id)
            {
                CharacterSheetOwnerSource? evidence = null;
                Require(ownerSources is not null && ownerSources.TryResolveSheetOwnerSource("WeaponAccessory", id, out evidence) && evidence is not null,
                    "accessory-source-unavailable");
                Require(evidence.SourceXml.Length <= CharacterCreationLoadoutRules.MaximumSourceNodeLength, "accessory-source-unavailable");
                var node = Parse(evidence!.SourceXml);
                Require(evidence.Kind == "WeaponAccessory" && evidence.SourceId == id && id != Guid.Empty
                    && node.Name == "accessory" && Guid.Parse(Text(node, "id")) == id
                    && evidence.Name == Text(node, "name") && evidence.SourceBook == Text(node, "source")
                    && new[] { evidence.CatalogDigest, evidence.ProfileDigest, evidence.CustomDataDigest, evidence.PolicyDigest }
                        .All(d => !string.IsNullOrWhiteSpace(d)), "accessory-source-unavailable");
                if (attachmentWitnesses.TryGetValue(id, out var prior))
                    Require(Digest(prior) == Digest(evidence), "accessory-source-changed");
                attachmentWitnesses[id] = evidence;
                return evidence;
            }
            XElement Source(XElement row, string kind)
            {
                Require(Guid.TryParse(Text(row, "sourceid"), out var id) && id != Guid.Empty, "source-identity-invalid");
                if (kind == "gear")
                {
                    Require(context.TryResolveCareerGearPurchaseSource(id, out var evidence) && evidence is not null
                        && evidence.SourceId == id && !string.IsNullOrWhiteSpace(evidence.CatalogDigest)
                        && !string.IsNullOrWhiteSpace(evidence.ProfileDigest) && !string.IsNullOrWhiteSpace(evidence.CustomDataDigest), "ammunition-source-unavailable");
                    var gearSource = Parse(evidence!.SourceXml);
                    Require(gearSource.Name == "gear" && Guid.Parse(Text(gearSource, "id")) == id
                        && Text(row, "name") == evidence.Name && evidence.Name == Text(gearSource, "name")
                        && evidence.SourceBook == Text(gearSource, "source") && Text(row, "category") == Text(gearSource, "category"), "saved-ammunition-source-divergence");
                    if (gearWitnesses.TryGetValue(id, out var prior)) Require(Digest(prior) == Digest(evidence), "ammunition-source-changed");
                    gearWitnesses[id] = evidence;
                    return gearSource;
                }
                var options = catalog.Where(o => o.Kind == kind && o.SourceId == id).ToArray();
                Require(options.Length == 1 && CharacterCreationLoadoutRules.IsValidOption(options[0]), "source-identity-unavailable");
                var source = Parse(options[0].SourceNodeXml);
                Require(Guid.Parse(Text(source, "id")) == id && Text(row, "name") == Text(source, "name")
                    && Text(row, "category") == Text(source, "category"), "saved-source-divergence");
                return source;
            }
            var ammo = new List<Ammo>();
            var gearRoot = One(root, "gears") ?? One(root, "gear");
            foreach (var row in gearRoot?.Descendants("gear") ?? Enumerable.Empty<XElement>())
            {
                try
                {
                    var source = Source(row, "gear");
                    if (One(source, "ammoforweapontype") is null) continue;
                    Require(!row.Descendants("gear").Any(), "ammunition-child-effects-unavailable");
                    Require(Boolean(row, "equipped", true), "ammunition-not-equipped");
                    // A single canonical spare clip/speed loader may contain rounds. Stacked containers
                    // need a split identity transaction, deliberately not synthesized by a session action.
                    foreach (var parent in row.Ancestors("gear").Where(g => g != gearRoot))
                    {
                        var container = Source(parent, "gear");
                        Require(Text(container, "name") is "Spare Clip" or "Speed Loader"
                            && Quantity(parent) == 1 && Boolean(parent, "equipped", true), "ammunition-container-unavailable");
                    }
                    string types = Text(source, "ammoforweapontype");
                    Require(Text(row, "ammoforweapontype", types) == types
                        && Boolean(row, "isflechetteammo", false) == Boolean(source, "isflechetteammo", false), "ammunition-source-divergence");
                    // Reject source effects that could change consumption/modes; ordinary damage/AP
                    // metadata is retained only as provenance and never applied as numeric effects here.
                    Require(One(source, "bonus")?.HasElements != true && One(source, "wirelessbonus")?.HasElements != true
                        && !source.Descendants().Any(e => e.Name.LocalName is "modereplace" or "mode" or "ammoreplace"
                            or "firemode" or "firemodereplace" or "ammobonus" or "modifyammocapacity" or "singleshot" or "shortburst"
                            or "longburst" or "fullburst" or "suppressive"), "ammunition-effect-adapter-unavailable");
                    foreach (string effect in new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus", "wirelessweaponbonus" })
                    {
                        var canonical = One(source, effect); var saved = One(row, effect);
                        Require(canonical?.HasElements == true ? saved is not null && XNode.DeepEquals(canonical, saved)
                            : saved?.HasElements != true, "ammunition-source-divergence");
                    }
                    int quantity = Quantity(row);
                    ammo.Add(new(row, source, new(Id(row), Guid.Parse(Text(source, "id")),
                        Text(row, "name") + (Text(row, "extra", "") is { Length: > 0 } extra ? " (" + extra + ")" : ""), quantity, quantity)));
                }
                catch (Exception e) when (SourceFailure(e))
                { diagnostics.Add(new("ammunition-source-unavailable", "ammo-reload", Describe(row) + ": needs exact source, whole-round quantity, equipped state and supported container/effect authority.")); }
            }
            Require(ammo.Count <= 256, "ammunition-inventory-capacity");
            foreach (var row in owned)
            {
                try
                {
                    Require(Boolean(row, "equipped", true) && !row.Descendants("weapon").Any() && Text(row, "parentid", "").Length == 0
                        && !Boolean(row, "cyberware", false) && One(row, "weaponaccessories")?.HasElements != true
                        && One(row, "bonus")?.HasElements != true
                        && (One(root, "improvements")?.HasElements != true || ReferenceEquals(context, provenSheetContext)),
                        "weapon-modifier-authority-unavailable");
                    var source = Source(row, "weapon");
                    Require(One(source, "bonus")?.HasElements != true && One(source, "wirelessbonus")?.HasElements != true
                        && One(source, "underbarrels")?.HasElements != true && !Boolean(source, "cyberware", false)
                        && !source.Descendants("weapon").Any()
                        && !source.Descendants("addweapon").Any(), "weapon-source-effects-unavailable");
                    var option = catalog.Single(o => o.Kind == "weapon" && o.SourceId == Guid.Parse(Text(row, "sourceid")));
                    var category = Parse(option.SourceCategoryXml);
                    string type = Text(source, "weapontype", (string?)category.Attribute("type") ?? Text(source, "category").ToLowerInvariant());
                    string sourceCategory = Text(source, "ammocategory", ""), savedCategory = Text(row, "ammocategory", "");
                    string ammoCategory = sourceCategory.Length == 0 ? Text(source, "category") : sourceCategory;
                    string ammoSpec = Text(source, "ammo"), modes = Text(source, "mode"), range = Text(source, "type");
                    Require(RoundTypes.Contains(type) && range == "Ranged" && Boolean(source, "requireammo", true) && Boolean(row, "requireammo", true) && Text(row, "weapontype") == type
                        && (savedCategory.Length == 0 ? Text(row, "category") : savedCategory) == ammoCategory
                        && Text(row, "ammo") == ammoSpec && Text(row, "mode") == modes && Text(row, "type") == range
                        && Text(row, "damage") == Text(source, "damage"), "weapon-saved-source-divergence");
                    var attachments = ResolveAttachments(row, source, AttachmentSource, identity =>
                    {
                        CharacterSheetOwnerSource? named = null;
                        Require(ownerSources is not null && ownerSources.TryResolveSheetNamedSource("WeaponAccessory", identity, out named)
                            && named is not null, "accessory-intrinsic-divergence");
                        Require(Digest(AttachmentSource(named.SourceId)) == Digest(named), "accessory-source-changed");
                        return named.SourceId;
                    }, (child, declaration) =>
                    {
                        var childSource = Source(child, "gear");
                        Require(ownerSources is not null && ownerSources.TryResolveSheetNamedSource("Gear", Text(declaration, "name"), out var named)
                            && named is not null && named.SourceId == Guid.Parse(Text(child, "sourceid"))
                            && XNode.DeepEquals(Parse(named.SourceXml), childSource), "accessory-child-source-unavailable");
                        return childSource;
                    });
                    var feeds = EffectiveFeeds(ammoSpec, attachments);
                    Require(Integer(row, "activeammoslot") == 1 && Integer(row, "ammoslots", 1) == 1
                        && Integer(source, "ammoslots", 1) == 1, "weapon-slot-unavailable");
                    var clipContainer = One(row, "clips");
                    var clips = clipContainer?.Elements("clip").ToArray() ?? [];
                    Require(clipContainer is null || !clipContainer.HasAttributes
                        && clipContainer.Nodes().All(node => node is XElement element && element.Name == "clip"
                            || node is XText text && string.IsNullOrWhiteSpace(text.Value))
                        && clips.All(clip => !clip.HasAttributes && clip.Elements().Count() == 2
                            && clip.Nodes().All(node => node is XElement field && (field.Name == "id" || field.Name == "count")
                                || node is XText text && string.IsNullOrWhiteSpace(text.Value))
                            && One(clip, "id") is not null && One(clip, "count") is not null), "weapon-clip-unavailable");
                    // Weapon.Save omits <clips> when every clip is empty. With modern version,
                    // one canonical slot and no legacy fields this is an explicit empty allocation.
                    Require(clips.Length == 1 && Guid.TryParse(Text(clips[0], "id"), out _)
                        || clips.Length == 0 && One(row, "clips") is null, "weapon-clip-unavailable");
                    int count = clips.Length == 0 ? 0 : Integer(clips[0], "count");
                    var gearId = clips.Length == 0 ? Guid.Empty : Guid.Parse(Text(clips[0], "id"));
                    Require(count >= 0 && count <= feeds.Max(f => f.Capacity) && (count == 0 || gearId != Guid.Empty), "weapon-ammo-invalid");
                    bool flechette = Text(source, "damage").Contains("(f)", StringComparison.Ordinal);
                    var compatible = ammo.Where(a => Text(a.Source, "ammoforweapontype").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(type, StringComparer.Ordinal)
                        && (Text(a.Row, "extra", "").Length == 0 || Text(a.Row, "extra", "") == ammoCategory)
                        && (!flechette || Boolean(a.Source, "isflechetteammo", false))).ToArray();
                    Require(gearId == Guid.Empty || compatible.Any(a => a.Item.GearId == gearId), "loaded-ammunition-incompatible");
                    bool Flag(string name) { bool canonical = Boolean(source, name, true); Require(Boolean(row, name) == canonical, "weapon-fire-source-divergence"); return canonical; }
                    int Rounds(string name, int fallback) { int canonical = Integer(source, name, fallback); Require(Integer(row, name) == canonical, "weapon-fire-source-divergence"); return canonical; }
                    var fireSource = new CharacterWeaponFireSource(range, ammoSpec, modes, Flag("allowsingleshot"), Flag("allowshortburst"),
                        Flag("allowlongburst"), Flag("allowfullburst"), Flag("allowsuppressive"), Rounds("singleshot", 1), Rounds("shortburst", 3),
                        Rounds("longburst", 6), Rounds("fullburst", 10), Rounds("suppressive", 20), []);
                    decimal? quantity = gearId == Guid.Empty ? null : compatible.Single(a => a.Item.GearId == gearId).Item.TotalRounds;
                    Require(CharacterWeaponFireRules.TryCreateState(new(Id(row), 1, gearId), true, Text(row, "name"), count, quantity, fireSource, false, out var state), "weapon-fire-authority-unavailable");
                    var configuration = attachments.Count == 0 ? null : new SessionPlayWeaponConfiguration(option.SourceId,
                        Digest(new { Schema = "sr5-local-weapon-configuration/v1", option, authority.AuthorityDigest,
                            Attachments = attachments.Select(a => new { a.Evidence, a.Selection, a.ChildDigest }).ToArray(), feeds }),
                        attachments.Select(a => a.Selection).ToArray());
                    if (configuration is not null) state = state with { Revision = Digest(new { state.Revision, configuration }) };
                    weapons.Add(state);
                    // Multi-feed saves do not encode the chosen feed. Require explicit selection on first reload;
                    // a same-ammo top-up cannot choose a feed smaller than the current loaded allocation.
                    custody.Add(new(Id(row), 1, Text(row, "name"), feeds, compatible.Select(a => a.Item.GearId).ToArray(), gearId, count,
                        feeds.Count == 1 ? feeds[0].Id : null) { Configuration = configuration });
                }
                catch (Exception e) when (SourceFailure(e))
                { diagnostics.Add(new("weapon-ammunition-unavailable", "ammo-reload", Describe(row) + ": " + UnavailableReason(e))); }
            }
            // Ammunition referenced by unsupported clips cannot be borrowed by a supported weapon.
            var supported = custody.Select(w => w.WeaponId).ToHashSet();
            var reserved = new HashSet<Guid>();
            foreach (var clip in root.Descendants("clip"))
            {
                Require(Guid.TryParse(Text(clip, "id"), out var gearId) && Integer(clip, "count") >= 0, "clip-custody-invalid");
                var weapon = clip.Ancestors("weapon").FirstOrDefault();
                if (weapon is null || !supported.Contains(Id(weapon))) reserved.Add(gearId);
            }
            var removed = custody.Where(w => reserved.Contains(w.LoadedGearId) && w.LoadedGearId != Guid.Empty).Select(w => w.WeaponId).ToHashSet();
            foreach (var w in custody.Where(w => removed.Contains(w.WeaponId))) reserved.Add(w.LoadedGearId);
            weapons.RemoveAll(w => removed.Contains(w.Identity.WeaponId)); custody.RemoveAll(w => removed.Contains(w.WeaponId));
            ammo.RemoveAll(a => reserved.Contains(a.Item.GearId));
            custody = custody.Select(w => w with { CompatibleGearIds = w.CompatibleGearIds.Where(id => !reserved.Contains(id)).ToArray() }).ToList();
            var inventory = ammo.Select(a => a.Item with { AvailableRounds = checked(a.Item.TotalRounds
                - custody.Where(w => w.LoadedGearId == a.Item.GearId).Sum(w => w.LoadedRounds)) }).ToArray();
            Require(inventory.All(a => a.AvailableRounds >= 0), "loaded-ammunition-overallocated");
            Require(context.TryResolveCreationLoadoutAuthority(out var final) && final.AuthorityDigest == authority.AuthorityDigest, "weapon-source-changed");
            foreach (var witness in gearWitnesses)
                Require(context.TryResolveCareerGearPurchaseSource(witness.Key, out var latest) && latest is not null
                    && Digest(latest) == Digest(witness.Value), "ammunition-source-changed");
            foreach (var witness in attachmentWitnesses)
                Require(ownerSources!.TryResolveSheetOwnerSource("WeaponAccessory", witness.Key, out var latest) && latest is not null
                    && Digest(latest) == Digest(witness.Value), "accessory-source-changed");
            var sources = gearWitnesses.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
            // Keep the prior digest shape when this resolver has no new attachment witnesses.
            string revision = attachmentWitnesses.Count == 0 ? Digest(new { inventory, custody, sources })
                : Digest(new { inventory, custody, sources, attachments = attachmentWitnesses.OrderBy(x => x.Key).Select(x => x.Value).ToArray() });
            ammunition = new(revision, inventory, custody);
            diagnostics.Add(new("ammunition-local-custody", "ammo-reload", "Round custody and consumption persist only in this device's play ledger/history. Character XML and PDF exports remain canonical and do not include these round changes. No damage or ammunition effects are applied."));
            if (reserved.Count > 0) diagnostics.Add(new("ammunition-reserved", "ammo-reload", "Rounds held by unsupported clips are unavailable for transfer."));
            return weapons;
        }
        catch (Exception e) when (SourceFailure(e))
        {
            diagnostics.Add(new("ammunition-authority-unavailable", "ammo-reload", "Ammunition custody cannot be proved for this saved character. Exact identities, a migrated save and current source authority are required."));
            return [];
        }
    }
    private sealed record Attachment(XElement Source, CharacterSheetOwnerSource Evidence, SessionPlayWeaponAttachment Selection, string ChildDigest);

    // This is a vocabulary of source semantics, never a list of accessory names. Attack-only
    // scalar effects remain provenance; custody never presents their numbers as applied effects.
    private static readonly HashSet<string> AccessoryFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "source", "page", "altpage", "avail", "cost", "weight", "rating", "ratinglabel",
        "mount", "extramount", "required", "forbidden", "notes", "altnotes", "notesColor", "conceal", "rc", "rcgroup",
        "rcdeployable", "accuracy", "dicepool", "damage", "damagetype", "damagereplace", "ap", "apreplace", "reach",
        "rangebonus", "rangemodifier", "ammobonus", "ammoreplace", "modifyammocapacity", "ammoslots", "extra",
        "firemode", "firemodereplace", "addmode", "singleshot", "shortburst", "longburst", "fullburst", "suppressive",
        "replacerange", "addmount", "specialmodification", "accessorycostmultiplier", "gears", "allowgear"
    };
    private static readonly string[] BoundAccessoryFields =
        ["ammobonus", "ammoreplace", "modifyammocapacity", "firemode", "firemodereplace", "replacerange", "addmount", "extra",
         "rc", "conceal", "dicepool", "damage", "damagetype", "damagereplace", "ap", "apreplace", "accuracy", "reach"];

    private static IReadOnlyList<Attachment> ResolveAttachments(XElement weapon, XElement source,
        Func<Guid, CharacterSheetOwnerSource> resolve, Func<string, Guid> resolveIntrinsic, Func<XElement, XElement, XElement> resolveChild)
    {
        var saved = Children(weapon, "accessories", "accessory");
        var intrinsic = Children(source, "accessories", "accessory");
        Require(saved.Length <= 32 && intrinsic.Length <= saved.Length, "accessory-identity-unavailable");
        var result = new List<Attachment>();
        foreach (var row in saved)
        {
            Require(Guid.TryParse(Text(row, "sourceid"), out var id) && id != Guid.Empty, "accessory-identity-unavailable");
            var evidence = resolve(id); var canonical = Parse(evidence.SourceXml);
            Require(Text(row, "name") == evidence.Name && Text(row, "source", evidence.SourceBook) == evidence.SourceBook
                && (Text(row, "parentid", "").Length == 0 || Text(row, "parentid") == Id(weapon).ToString("D")), "accessory-owner-divergence");
            Require(!canonical.HasAttributes && canonical.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                && canonical.Elements().All(e => e.Name.NamespaceName.Length == 0 && AccessoryFields.Contains(e.Name.LocalName))
                && canonical.Elements().Where(e => e.Name.LocalName is not ("required" or "forbidden" or "gears" or "allowgear"))
                    .All(e => !e.HasAttributes && !e.HasElements), "accessory-effects-unavailable");
            // Gear/grants/wireless/improvements need their own closure. Even unequipped unknown
            // children cannot be silently dropped and later reactivated by a persisted option.
            Require(!row.Descendants("weapon").Any()
                && new[] { "bonus", "wirelessbonus", "wirelessweaponbonus", "weaponbonus" }
                    .All(n => One(row, n)?.HasElements != true), "accessory-effects-unavailable");
            string childDigest = ResolvePassiveChildren(row, canonical, resolveChild);
            bool included = Boolean(row, "included", false), equipped = Boolean(row, "equipped");
            int rating = Integer(row, "rating"), maximum = Integer(canonical, "rating", 0);
            Require(maximum >= 0 && rating >= 0 && rating <= maximum && (included || maximum == 0 || rating > 0), "accessory-selection-invalid");
            Require(Text(row, "maxrating", maximum.ToString(CultureInfo.InvariantCulture)) == maximum.ToString(CultureInfo.InvariantCulture),
                "accessory-selection-invalid");
            foreach (string field in BoundAccessoryFields)
            {
                string expected = Text(canonical, field, "");
                if (field is "accuracy" or "reach" && expected is "0" or "+0" or "-0") expected = "";
                Require(Text(row, field, "") == expected, "accessory-saved-source-divergence");
            }
            foreach (string field in new[] { "rangebonus", "rangemodifier" })
                Require(Text(row, field, "0") == Text(canonical, field, "0"), "accessory-saved-source-divergence");
            foreach (string field in new[] { "ammoslots", "singleshot", "shortburst", "longburst", "fullburst", "suppressive", "rcgroup" })
                Require(Integer(row, field, 0) == Integer(canonical, field, 0), "accessory-saved-source-divergence");
            Require(Boolean(row, "rcdeployable", false) == Boolean(canonical, "rcdeployable", false)
                && Boolean(row, "specialmodification", false) == Boolean(canonical, "specialmodification", false), "accessory-saved-source-divergence");
            Require(Integer(canonical, "ammoslots", 0) == 0, "weapon-slot-unavailable");
            Require(!Boolean(canonical, "specialmodification", false)
                && new[] { "firemode", "firemodereplace", "addmode", "replacerange", "addmount", "extra" }
                    .All(n => Text(canonical, n, "").Length == 0)
                && new[] { "singleshot", "shortburst", "longburst", "fullburst", "suppressive" }
                    .All(n => Integer(canonical, n, 0) == 0), "accessory-effects-unavailable");
            result.Add(new(canonical, evidence, new(Id(row), id, Id(weapon), evidence.Name, included, equipped, rating,
                Text(row, "mount"), Text(row, "extramount", "None"), Text(row, "extra", "")), childDigest));
        }
        Require(result.Select(a => a.Selection.InstanceId).Distinct().Count() == result.Count, "accessory-identity-unavailable");
        // Intrinsic references are source-owned names/IDs, not compatibility guesses. Match
        // the canonical receipt already resolved by the saved source ID; ambiguous names fail.
        var matched = new HashSet<Guid>();
        foreach (var declaration in intrinsic)
        {
            Require(!declaration.HasAttributes && declaration.Elements().All(e => e.Name.LocalName is "name" or "rating" or "mount" or "extramount"),
                "accessory-effects-unavailable");
            Guid identity = resolveIntrinsic(Text(declaration, "name"));
            var matches = result.Where(a => a.Selection.Included && !matched.Contains(a.Selection.InstanceId)
                && identity == a.Selection.SourceId
                && a.Selection.Rating == Math.Min(Integer(declaration, "rating", 0), Integer(a.Source, "rating", 0))
                && a.Selection.Mount == (One(declaration, "mount") is null ? "Internal" : Text(a.Source, "mount"))
                && a.Selection.ExtraMount == (One(declaration, "extramount") is null ? "None" : Text(a.Source, "extramount"))).ToArray();
            Require(matches.Length > 0, "accessory-intrinsic-divergence");
            var item = matches[0]; matched.Add(item.Selection.InstanceId);
        }
        Require(result.Count(a => a.Selection.Included) == matched.Count, "accessory-intrinsic-divergence");
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var mounts = Children(source, "accessorymounts", "mount").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var item in result)
        {
            if (!item.Selection.Included)
            {
                Require(Boolean(source, "allowaccessory", true), "accessory-mount-unavailable");
                CheckMount(item.Selection.Mount, Text(item.Source, "mount", ""));
                CheckMount(item.Selection.ExtraMount, Text(item.Source, "extramount", ""));
                Require(AccessoryRequirements(item.Source, source, result), "accessory-requirements-unavailable");
            }
            foreach (var mount in new[] { item.Selection.Mount, item.Selection.ExtraMount })
                if (mount is not ("" or "None" or "Internal")) Require(occupied.Add(mount), "accessory-mount-unavailable");
        }
        return result;
        void CheckMount(string selected, string choices)
        {
            Require(choices.Length == 0 ? selected is "" or "None"
                : choices.Split('/').Contains(selected, StringComparer.Ordinal) && mounts.Contains(selected), "accessory-mount-unavailable");
        }
    }

    private static readonly HashSet<string> PassiveGearFields = new(StringComparer.Ordinal)
    {
        "id", "name", "category", "rating", "source", "page", "avail", "cost", "costfor", "capacity", "armorcapacity",
        "addoncategory", "requireparent", "required", "translate", "altpage", "notes", "altnotes", "weight"
    };
    private static string ResolvePassiveChildren(XElement accessory, XElement canonical, Func<XElement, XElement, XElement> resolve)
    {
        var declarations = Children(canonical, "gears", "usegear"); var children = Children(accessory, "gears", "gear");
        Require(children.Length == declarations.Length && children.Length <= 8, "accessory-child-source-unavailable");
        var matched = new HashSet<Guid>();
        var evidence = new List<string>();
        foreach (var declaration in declarations)
        {
            Require(!declaration.HasAttributes && declaration.Elements().All(e => e.Name.LocalName is "name" or "category" or "rating")
                && Integer(declaration, "rating", 0) == 0, "accessory-child-source-unavailable");
            var candidates = children.Where(c => Text(c, "name") == Text(declaration, "name")
                && Text(c, "category") == Text(declaration, "category")).ToArray();
            Require(candidates.Length == 1 && matched.Add(Id(candidates[0])), "accessory-child-source-unavailable");
            var child = candidates[0]; var source = resolve(child, declaration);
            Require(Text(child, "parentid") == Id(accessory).ToString("D") && Quantity(child) == 1
                && Integer(child, "rating") == 0 && Integer(source, "rating") == 0 && Text(child, "extra", "").Length == 0
                && !child.Descendants("gear").Any() && !child.Descendants("weapon").Any()
                && new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus", "wirelessweaponbonus" }
                    .All(n => One(child, n)?.HasElements != true)
                && !source.HasAttributes && source.Elements().All(e => e.Name.NamespaceName.Length == 0 && PassiveGearFields.Contains(e.Name.LocalName))
                && source.Elements().Where(e => e.Name != "required").All(e => !e.HasElements && !e.HasAttributes),
                "accessory-child-source-unavailable");
            evidence.Add(Digest(new { Saved = child.ToString(SaveOptions.DisableFormatting), Source = source.ToString(SaveOptions.DisableFormatting) }));
        }
        return Digest(evidence);
    }

    private static XElement[] Children(XElement parent, string container, string kind)
    {
        var node = One(parent, container);
        if (node is null) return [];
        Require(!node.HasAttributes && node.Elements().All(e => e.Name == kind)
            && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "ambiguous-source-field");
        return node.Elements().ToArray();
    }

    private static bool AccessoryRequirements(XElement accessory, XElement weapon, IReadOnlyList<Attachment> attachments)
    {
        bool Match(XElement node)
        {
            Require(!node.HasAttributes && node.HasElements
                && node.Elements().All(e => e.Name.NamespaceName.Length == 0), "accessory-requirements-unavailable");
            // Evaluate every branch, including ineligible ones, to reject unknown vocabulary.
            var values = node.Elements().Select(child => child.Name.LocalName switch
            {
                "weapondetails" => WeaponPredicate(child, weapon),
                "oneof" => AccessorySet(child, false),
                "allof" => AccessorySet(child, true),
                _ => throw new SessionPlayException(SessionPlayFailures.Invalid("accessory-requirements-unavailable"))
            }).ToArray();
            return node.Name == "forbidden" ? values.Any(v => v) : values.All(v => v);
        }
        bool AccessorySet(XElement node, bool all)
        {
            Require(!node.HasAttributes && node.HasElements, "accessory-requirements-unavailable");
            var values = node.Elements().Select(child =>
            {
                Require(child.Name == "accessory" && !child.HasAttributes && !child.HasElements, "accessory-requirements-unavailable");
                return attachments.Any(a => a.Evidence.Name == child.Value);
            }).ToArray();
            return all ? values.All(v => v) : values.Any(v => v);
        }
        var forbidden = One(accessory, "forbidden"); var required = One(accessory, "required");
        bool denied = forbidden is not null && Match(forbidden), eligible = required is null || Match(required);
        return !denied && eligible;
    }
    private static readonly HashSet<string> WeaponPredicateFields = new(StringComparer.Ordinal)
        { "id", "name", "category", "type", "ammo", "ammocategory", "mode", "spec", "spec2", "weapontype" };
    private static bool WeaponPredicate(XElement node, XElement weapon)
    {
        Require(!node.HasAttributes && node.HasElements, "accessory-requirements-unavailable");
        var values = node.Elements().Select(child =>
        {
            Require(child.Name.NamespaceName.Length == 0, "accessory-requirements-unavailable");
            if (child.Name.LocalName is "OR" or "AND") return WeaponPredicate(child, weapon);
            Require(!child.HasElements && WeaponPredicateFields.Contains(child.Name.LocalName)
                && child.Attributes().All(a => a.Name == "operation"), "accessory-requirements-unavailable");
            string operation = (string?)child.Attribute("operation") ?? "==";
            Require(operation is "==" or "equals" or "contains", "accessory-requirements-unavailable");
            var field = One(weapon, child.Name.LocalName);
            if (field is null) return false;
            string value = Text(weapon, child.Name.LocalName).Trim(), expected = child.Value.Trim();
            return operation == "contains" ? value.Length > 0 && expected.Length > 0 && value.Contains(expected, StringComparison.OrdinalIgnoreCase)
                : value == expected;
        }).ToArray();
        return node.Name == "OR" ? values.Any(v => v) : values.All(v => v);
    }

    private static IReadOnlyList<SessionPlayAmmoFeed> EffectiveFeeds(string spec, IReadOnlyList<Attachment> attachments)
    {
        // Validate base even when a replacement is present: replacement cannot turn an external
        // power source, multi-barrel multiplier or unknown feed into ordinary physical custody.
        var original = ParseFeeds(spec);
        string effective = spec; decimal percent = 0;
        var modifications = new List<string>();
        foreach (var item in attachments.Where(a => a.Selection.Equipped))
        {
            string replacement = Text(item.Source, "ammoreplace", "");
            if (replacement.Length > 0) effective = replacement;
            string bonus = Text(item.Source, "ammobonus", "");
            if (bonus.Length > 0) percent = checked(percent + CapacityExpression(bonus.Replace("Rating",
                item.Selection.Rating.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).TrimStart('+')));
            string modify = Text(item.Source, "modifyammocapacity", "");
            if (modify.Length > 0) modifications.Add(modify);
        }
        var feeds = ParseFeeds(effective);
        if (percent == 0 && modifications.Count == 0) return feeds;
        var result = new List<SessionPlayAmmoFeed>();
        foreach (var feed in feeds)
        {
            int separator = feed.Id.IndexOf('(');
            Require(separator > 0, "weapon-feed-expression-unavailable");
            string expression = feed.Capacity.ToString(CultureInfo.InvariantCulture);
            if (modifications.Count > 0)
            {
                var builder = new StringBuilder("(" + expression);
                foreach (string modify in modifications)
                {
                    builder.Append(modify).Append(')');
                    int balance = modify.Count(c => c == ')') - modify.Count(c => c == '(') + 1;
                    if (balance > 0) builder.Insert(0, new string('(', balance));
                    else if (balance < 0) builder.Append(')', -balance);
                }
                builder.Append(')'); expression = builder.ToString();
                if (expression.Contains("Weapon", StringComparison.Ordinal))
                {
                    Require(original.Count == 1, "weapon-feed-expression-unavailable");
                    expression = expression.Replace("Weapon", original[0].Capacity.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                }
            }
            decimal capacity = CapacityExpression(expression);
            capacity = checked(capacity + capacity * percent / 100m);
            // Legacy DecimalExtensions.StandardRound is ceiling for positive values, NOT nearest.
            Require(capacity > 0 && capacity <= int.MaxValue, "weapon-feed-expression-unavailable");
            int rounds = decimal.ToInt32(decimal.Ceiling(capacity));
            result.Add(new(rounds.ToString(CultureInfo.InvariantCulture) + feed.Id[separator..], rounds));
        }
        Require(result.Select(f => f.Id).Distinct().Count() == result.Count, "weapon-feed-ambiguous");
        return result;
    }

    private static decimal CapacityExpression(string expression)
    {
        Require(expression.Length is > 0 and <= 512
            && Regex.IsMatch(expression, @"\A(?:[0-9.()+*\-\s]|div)+\z", RegexOptions.CultureInvariant), "weapon-feed-expression-unavailable");
        if (decimal.TryParse(expression, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var scalar)) return scalar;
        // Same XPath double -> decimal arithmetic as legacy, restricted to numeric tokens.
        object value = new XElement("capacity").XPathEvaluate(expression);
        Require(value is double number && double.IsFinite(number), "weapon-feed-expression-unavailable");
        return Convert.ToDecimal((double)value);
    }

    private static string UnavailableReason(Exception error)
    {
        string code = error is SessionPlayException failure ? failure.Failure.Code : "";
        return code switch
        {
            "accessory-source-unavailable" or "accessory-source-changed" => "An attachment's current rules source could not be verified.",
            "accessory-owner-divergence" or "accessory-identity-unavailable" or "accessory-intrinsic-divergence" => "Attachment identity or ownership does not match this weapon's rules source.",
            "accessory-saved-source-divergence" or "accessory-selection-invalid" => "Saved attachment options differ from the supported rules source.",
            "accessory-mount-unavailable" or "accessory-requirements-unavailable" => "The selected attachment mounts or requirements could not be verified.",
            "accessory-effects-unavailable" => "This attachment has child gear, special or mode-changing effects that local ammunition play cannot resolve yet.",
            "accessory-child-source-unavailable" => "Attachment gear needs exact passive source and parent ownership; selected or effect-bearing children are not supported yet.",
            "weapon-slot-unavailable" => "Multiple ammunition slots need separate custody support.",
            "weapon-feed-unavailable" or "weapon-feed-expression-unavailable" or "weapon-feed-ambiguous" => "The feed capacity is not a supported, unambiguous physical-round value.",
            _ => "Needs source-matched physical rounds and one directly owned feed. Vehicle, magical, child and external-source weapons remain unavailable."
        };
    }
    private static string Describe(XElement row) => new string((row.Element("name")?.Value ?? "Inventory item")
        .Where(c => !char.IsControl(c)).Take(96).ToArray());
    private static bool SourceFailure(Exception e) => e is SessionPlayException or InvalidOperationException or FormatException or OverflowException or ArgumentException
        or System.Xml.XmlException or XPathException;
    private static Guid Id(XElement row) { Require(Guid.TryParse(Text(row, "guid"), out var id) && id != Guid.Empty, "inventory-identity-invalid"); return id; }
    private static int Quantity(XElement row)
    {
        Require(decimal.TryParse(Text(row, "qty"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var q)
            && q >= 0 && q <= int.MaxValue && q == decimal.Truncate(q), "ammunition-quantity-invalid"); return (int)q;
    }
    internal static IReadOnlyList<SessionPlayAmmoFeed> ParseFeeds(string spec)
    {
        var result = new List<SessionPlayAmmoFeed>();
        foreach (var part in spec.Replace(" or ", "/", StringComparison.Ordinal).Split('/'))
        {
            var match = Regex.Match(part, @"^([1-9][0-9]*)(?:\((c|m|cy|b|ml|d|belt)\))?$", RegexOptions.CultureInvariant);
            Require(match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _), "weapon-feed-unavailable");
            result.Add(new(part, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        Require(result.Count is > 0 and <= 4 && result.Select(f => f.Id).Distinct().Count() == result.Count, "weapon-feed-ambiguous");
        return result;
    }
}
