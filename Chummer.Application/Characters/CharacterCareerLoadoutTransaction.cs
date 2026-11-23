using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Loadout planning inside the existing atomic Gear/original-operation protocol.</summary>
public static class CharacterCareerLoadoutTransaction
{
    public const int MaximumPresets = 32;
    public const int MaximumItems = 10000;
    private static string Hash(string value) => CharacterCareerGearEquipmentTransaction.Hash(value);
    private static bool Same<T>(T a, T b) => CharacterCareerGearEquipmentTransaction.Same(a, b);
    public static bool IsValid(IReadOnlyList<CharacterCareerLoadoutPreset>? presets)
        => presets is null || presets.Count <= MaximumPresets
            && presets.Select(p => p?.Id).Distinct().Count() == presets.Count
            && presets.All(p => p is not null && p.Id != Guid.Empty && ValidName(p.Name)
                && p.Items is { Count: <= MaximumItems }
                && p.Items.All(i => i is not null && i.Identity is { Length: > 0 and <= 8192 }
                    && CharacterCareerGearEquipmentTransaction.IsDigest(i.Fingerprint))
                && p.Items.Select(i => i.Identity).Distinct(StringComparer.Ordinal).Count() == p.Items.Count)
            && JsonSerializer.SerializeToUtf8Bytes(presets).Length <= 1024 * 1024;
    private static bool ValidName(string? name) => name is { Length: > 0 and <= 80 }
        && name == name.Trim() && !name.Any(char.IsControl);

    public static CharacterCareerLoadoutCatalog Capture(string xml,
        IReadOnlyList<CharacterCareerLoadoutPreset>? presets, IReadOnlyList<CharacterGearEquipmentState> gears)
    {
        if (!IsValid(presets)) throw new InvalidOperationException("Invalid loadout configuration.");
        var document = CharacterGearEquipmentCapture.Parse(xml);
        var root = document.Root!;
        var supported = new Dictionary<XElement, CharacterGearEquipmentState>();
        var gearStates = gears.ToDictionary(g => string.Join("/", g.Identity.GearPath), StringComparer.Ordinal);
        void IndexGear(XElement node, IReadOnlyList<Guid> ancestors)
        {
            Guid[] path = [.. ancestors, Id(node)];
            if (!gearStates.TryGetValue(string.Join("/", path), out var state))
                throw new InvalidOperationException("Gear projection changed.");
            supported.Add(node, state);
            foreach (var child in node.Elements("children").SingleOrDefault()?.Elements("gear") ?? []) IndexGear(child, path);
        }
        foreach (var node in root.Elements("gears").SingleOrDefault()?.Elements("gear") ?? []) IndexGear(node, []);
        if (supported.Count != gears.Count) throw new InvalidOperationException("Gear projection is incomplete.");
        var armorElements = root.Elements("armors").SingleOrDefault()?.Elements("armor").ToArray() ?? [];
        var armorBasis = armorElements.Select(a => new CharacterArmorEquipmentBasis(Id(a), Equipped(a)
            ?? throw new InvalidOperationException("Armor equipped flag is missing."))).ToArray();
        var nodes = new List<CharacterCareerLoadoutNode>();
        // These are saved-format containers, not item names or archetype lists.
        foreach (var container in root.Elements().Where(e => e.Name.LocalName is
            "gears" or "armors" or "weapons" or "cyberwares" or "biowares" or "vehicles"))
        {
            if (root.Elements(container.Name).Count() != 1) throw new InvalidOperationException("Ambiguous inventory container.");
            foreach (var element in container.Descendants().Where(e => e.Elements("guid").Any()
                || e.Elements("equipped").Any() || e.Name.LocalName is "gear" or "armor" or "weapon" or "cyberware" or "bioware" or "vehicle"))
            {
                if (nodes.Count >= MaximumItems) throw new InvalidOperationException("Inventory is too large.");
                string identity = Identity(element, root);
                var item = new CharacterCareerLoadoutItem(identity, Fingerprint(element), Equipped(element));
                string label = string.Join(" / ", element.AncestorsAndSelf().Reverse()
                    .Where(e => e.Elements("guid").Any()).Select(e => e.Element("name")?.Value ?? e.Name.LocalName));
                if (supported.TryGetValue(element, out var gear))
                    nodes.Add(new(item, label, gear.CanChangeEquip,
                        gear.CanChangeEquip ? null : "Included in a parent or loaded into a weapon clip; cannot toggle.", gear));
                else if (armorElements.Contains(element))
                {
                    Guid id = Id(element);
                    if (!CharacterArmorEquipmentRules.TryProject(id, armorBasis, out var armor) || armor is null)
                        throw new InvalidOperationException("Armor authority is unavailable.");
                    nodes.Add(new(item, label, true, null, ArmorId: id));
                }
                else nodes.Add(new(item, label, false,
                    "This saved equipment hierarchy has no typed equip mutation authority in this build; retained unchanged."));
            }
        }
        if (nodes.Select(n => n.Item.Identity).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
            throw new InvalidOperationException("Duplicate inventory identity.");
        return new(presets?.ToArray() ?? [], nodes.OrderBy(n => n.Item.Identity, StringComparer.Ordinal).ToArray());
    }

    private static Guid Id(XElement node)
    {
        var values = node.Elements("guid").ToArray();
        if (values.Length != 1 || !Guid.TryParseExact(values[0].Value.Trim(), "D", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Inventory needs a unique saved Guid.");
        return id;
    }
    private static bool? Equipped(XElement node)
    {
        var values = node.Elements("equipped").ToArray();
        if (values.Length == 0) return null;
        if (values.Length != 1 || !bool.TryParse(values[0].Value.Trim(), out var value))
            throw new InvalidOperationException("Inventory equipped flag is ambiguous.");
        return value;
    }
    private static string Identity(XElement element, XElement root)
    {
        var parts = new List<string>();
        foreach (var part in element.AncestorsAndSelf().Reverse().SkipWhile(e => e != root).Skip(1))
        {
            if (part.Name.Namespace != XNamespace.None) throw new InvalidOperationException("Unsupported inventory namespace.");
            if (part.Elements("guid").Any()) parts.Add(part.Name.LocalName + ":" + Id(part).ToString("D"));
            else
            {
                if (part.Parent!.Elements(part.Name).Count() != 1)
                    throw new InvalidOperationException("Inventory hierarchy has no unambiguous identity.");
                parts.Add(part.Name.LocalName);
            }
        }
        // Every inventory node itself requires a stable identity, including unsupported nodes.
        _ = Id(element);
        return string.Join("/", parts);
    }
    private static string Fingerprint(XElement element)
    {
        var copy = new XElement(element);
        foreach (var value in copy.Descendants("equipped")) value.Value = "#equipped#";
        // Hash normalization only. The actual source subtree is never stripped or rebuilt.
        return Hash(copy.ToString(SaveOptions.DisableFormatting));
    }

    public static CharacterCareerLoadoutReview Review(CharacterCareerLoadoutCatalog catalog,
        CharacterCareerLoadoutSelection selection)
    {
        var blockers = new List<string>();
        if (catalog is null || catalog.Presets is null || !IsValid(catalog.Presets)
            || catalog.Inventory is null || catalog.Inventory.Count > MaximumItems
            || catalog.Inventory.Any(n => n?.Item is null || n.Item.Identity is not { Length: > 0 and <= 8192 }
                || !CharacterCareerGearEquipmentTransaction.IsDigest(n.Item.Fingerprint))
            || catalog.Inventory.Select(n => n.Item.Identity).Distinct(StringComparer.Ordinal).Count() != catalog.Inventory.Count
            || selection is null || selection.PresetId == Guid.Empty || !Enum.IsDefined(selection.Action))
            return new(null, false, ["Invalid or ambiguous inventory/preset selection."]);
        var before = catalog.Presets.ToArray();
        var selected = before.SingleOrDefault(p => p.Id == selection.PresetId);
        var after = before;
        var changes = new List<CharacterCareerLoadoutChange>();
        switch (selection.Action)
        {
            case CharacterCareerLoadoutAction.Save:
                if (selected is not null || !ValidName(selection.Name)) blockers.Add("Choose a new preset identity and a name of 1–80 characters.");
                else after = before.Append(new CharacterCareerLoadoutPreset(selection.PresetId, selection.Name!,
                    catalog.Inventory.Select(n => n.Item).ToArray())).ToArray();
                break;
            case CharacterCareerLoadoutAction.Rename:
                if (selected is null || !ValidName(selection.Name)) blockers.Add("Select an existing preset and a valid name.");
                else after = before.Select(p => p.Id == selected.Id ? p with { Name = selection.Name! } : p).ToArray();
                break;
            case CharacterCareerLoadoutAction.Delete:
                if (selected is null || selection.Name is not null) blockers.Add("Select an existing preset.");
                else after = before.Where(p => p.Id != selected.Id).ToArray();
                break;
            case CharacterCareerLoadoutAction.Apply:
                if (selected is null || selection.Name is not null) { blockers.Add("Select an existing preset."); break; }
                var current = catalog.Inventory.ToDictionary(n => n.Item.Identity, StringComparer.Ordinal);
                var expected = selected.Items.ToDictionary(n => n.Identity, StringComparer.Ordinal);
                foreach (var item in selected.Items)
                {
                    if (!current.TryGetValue(item.Identity, out var node)) { blockers.Add("Missing inventory identity: " + item.Identity); continue; }
                    if (node.Item.Fingerprint != item.Fingerprint)
                    { blockers.Add(node.DisplayPath + ": source or configuration changed; save a new preset after review."); continue; }
                    if (node.Item.Equipped == item.Equipped) continue;
                    if (!node.CanChange || item.Equipped is null || node.Item.Equipped is null)
                    { blockers.Add(node.DisplayPath + ": cannot apply this equipped state. " + node.Limitation); continue; }
                    if (node.Gear is { } gear && !CharacterGearEquipmentRules.TryValidateMutation(gear, gear.Revision, item.Equipped.Value))
                    { blockers.Add(node.DisplayPath + ": gear rules reject the change."); continue; }
                    changes.Add(new(node, item.Equipped.Value));
                }
                foreach (var node in catalog.Inventory.Where(n => !expected.ContainsKey(n.Item.Identity)))
                    blockers.Add(node.DisplayPath + ": inventory was added; save a new preset after review.");
                break;
        }
        if (!IsValid(after)) blockers.Add("Preset storage limit or configuration is invalid.");
        return new(new(selection, before, after, changes), blockers.Count == 0 && Same(before, after) && changes.Count == 0, blockers);
    }

    public static bool IsConfirmed(CharacterCareerGearEquipmentCommand command)
    {
        var plan = command.Loadout;
        return plan is { BeforePresets: not null, AfterPresets: not null, Selection: not null }
            && command.Before is null && command.Request.Loadout is not null
            && Same(plan.Selection, command.Request.Loadout)
            && command.Request.Identity?.GearPath is { Count: 0 }
            && command.Request.ExpectedNodeRevision == "" && !command.Request.Equipped
            && IsValid(plan.BeforePresets) && IsValid(plan.AfterPresets) && ConfigurationTransition(plan)
            && plan.Changes is { Count: <= MaximumItems }
            && plan.Changes.All(c => c is { Before.CanChange: true } && c.Before.Item.Equipped.HasValue
                && c.Before.Item.Equipped != c.Equipped && (c.Before.Gear is not null || c.Before.ArmorId is not null))
            && plan.Changes.Select(c => c.Before.Item.Identity).Distinct(StringComparer.Ordinal).Count() == plan.Changes.Count
            && (!Same(plan.BeforePresets, plan.AfterPresets) || plan.Changes.Count != 0)
            && JsonSerializer.SerializeToUtf8Bytes(command).Length <= 1024 * 1024;
    }

    private static bool ConfigurationTransition(CharacterCareerLoadoutPlan plan)
    {
        var selection = plan.Selection;
        if (selection.PresetId == Guid.Empty || !Enum.IsDefined(selection.Action)) return false;
        if (selection.Action == CharacterCareerLoadoutAction.Apply)
            return selection.Name is null && plan.BeforePresets.Any(p => p.Id == selection.PresetId)
                && Same(plan.BeforePresets, plan.AfterPresets);
        if (plan.Changes is not { Count: 0 }) return false;
        if (selection.Action == CharacterCareerLoadoutAction.Save)
            return ValidName(selection.Name) && plan.BeforePresets.All(p => p.Id != selection.PresetId)
                && plan.AfterPresets.Count == plan.BeforePresets.Count + 1
                && plan.AfterPresets.Last().Id == selection.PresetId && plan.AfterPresets.Last().Name == selection.Name
                && Same(plan.BeforePresets, plan.AfterPresets.Take(plan.BeforePresets.Count).ToArray());
        var expected = Review(new(plan.BeforePresets, []), selection);
        return expected.Blockers.Count == 0 && !expected.NoOp && expected.Plan is not null
            && Same(expected.Plan.AfterPresets, plan.AfterPresets);
    }

    public static bool TryPlan(CharacterCareerGearEquipmentCatalog catalog, CharacterCareerGearEquipmentRequest request,
        out CharacterCareerGearEquipmentCommand? command)
    {
        command = null;
        if (catalog.Loadouts is null || request.Loadout is null) return false;
        var review = Review(catalog.Loadouts, request.Loadout);
        if (review.Blockers.Count != 0 || review.NoOp || review.Plan is null) return false;
        var candidate = new CharacterCareerGearEquipmentCommand(request, catalog.Binding, null, true, review.Plan);
        if (!CharacterCareerGearEquipmentTransaction.IsConfirmedCommand(candidate)) return false;
        command = candidate with { ExplicitlyConfirmed = false };
        return true;
    }

    // Called only after the Gear transaction compares the complete current binding,
    // rebuilds this exact plan, and rejects stale owner/revision/environment/ABA.
    public static WorkspaceDocument Apply(WorkspaceDocument original, CharacterCareerLoadoutPlan plan)
    {
        var document = CharacterGearEquipmentCapture.Parse(original.Content);
        var root = document.Root!;
        foreach (var change in plan.Changes)
        {
            XElement node = change.Before.Gear is { } gear
                ? CharacterGearEquipmentCapture.FindNode(root, gear.Identity)
                : root.Element("armors")!.Elements("armor").Single(a => Id(a) == change.Before.ArmorId);
            if (Identity(node, root) != change.Before.Item.Identity || Fingerprint(node) != change.Before.Item.Fingerprint
                || Equipped(node) != change.Before.Item.Equipped) throw new InvalidOperationException("Inventory changed.");
            if (change.Before.ArmorId is not null && !CharacterArmorEquipmentRules.CanApply(
                change.Equipped ? CharacterArmorEquipmentAction.EquipSelected : CharacterArmorEquipmentAction.UnequipSelected,
                change.Before.Item.Equipped!.Value, root.Element("armors")!.Elements("armor").Count(),
                root.Element("armors")!.Elements("armor").Count(a => Equipped(a) == true)))
                throw new InvalidOperationException("Armor rules reject the change.");
            node.Elements("equipped").Single().Value = change.Equipped ? "True" : "False";
        }
        return original with { State = original.State with
        {
            Payload = plan.Changes.Count == 0 ? original.Content : document.ToString(SaveOptions.DisableFormatting),
            AuxiliaryState = original.AuxiliaryState with { CharacterCareerLoadoutPresets = plan.AfterPresets.Count == 0 ? null : plan.AfterPresets }
        } };
    }
}
