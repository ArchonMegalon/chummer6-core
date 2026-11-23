using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Private compiler input. Observed absence is retained from the real original;
// no empty inventory is inserted to manufacture a traversal or source receipt.
internal sealed class CharacterCreationOwnedGearGraph1233
{
    private static readonly (string Container, string Entry)[] inventories =
        [("gears", "gear"), ("armors", "armor"), ("weapons", "weapon"), ("cyberwares", "cyberware"),
         ("vehicles", "vehicle"), ("foci", "focus"), ("stackedfoci", "stackedfocus")];
    private readonly XElement original;
    private readonly CharacterWorkspaceId owner1351;
    private readonly string linesDigest;
    private readonly XElement[] retained;
    private readonly XElement[] retainedFoci;
    private readonly XElement[] retainedStacks;
    private readonly CharacterCreationOriginalInventory3475? originalInventory3475;

    internal CharacterCreationOriginalInventory3475? OriginalLoadout3478
        => KeepsOriginalLoadout3475 ? originalInventory3475 : null;

    internal bool KeepsOriginalLoadout3475 => originalInventory3475 is not null
        && (!Empty(original, "weapons") || !Empty(original, "armors"));

    private CharacterCreationOwnedGearGraph1233(XElement original, CharacterWorkspaceId owner, IReadOnlyList<CharacterCreationGearLine> lines,
        CharacterCreationOriginalInventory3475? originalInventory3475)
    {
        this.originalInventory3475 = originalInventory3475;
        this.original = new(original);
        owner1351 = owner;
        linesDigest = CharacterCreationFinalizationDigest.Compute(lines);
        retained = original.Element("gears")?.Elements().Select(n => new XElement(n)).ToArray() ?? [];
        retainedFoci = original.Element("foci")?.Elements().Select(n => new XElement(n)).ToArray() ?? [];
        retainedStacks = original.Element("stackedfoci")?.Elements().Select(n => new XElement(n)).ToArray() ?? [];
    }

    internal static string GraphDigest(XElement root)
        => CharacterCreationFinalizationDigest.Compute(StructuralXml1277(root)!);

    // One structural representation for inventory capture, compiler verification
    // and retained-shape comparison. Only element-only indentation is ignored.
    // Leaf/player text, mixed content, CDATA, attributes and xml:space survive.
    // This is NOT the private raw XML / workspace / revision binding.
    private static string? StructuralXml1277(XElement? root)
    {
        if (root is null) return null; // absence is not a fabricated empty node
        var copy = new XElement(root);
        Normalize(copy, false);
        return copy.ToString(SaveOptions.DisableFormatting);

        static void Normalize(XElement element, bool preserve)
        {
            preserve = (string?)element.Attribute(XNamespace.Xml + "space") switch
            {
                "preserve" => true,
                "default" => false,
                _ => preserve
            };
            if (!preserve && element.HasElements
                && !element.Nodes().OfType<XText>().Any(t => t is XCData || !string.IsNullOrWhiteSpace(t.Value)))
                foreach (var text in element.Nodes().OfType<XText>().ToArray()) text.Remove();
            foreach (var child in element.Elements()) Normalize(child, preserve);
            if (!element.Nodes().Any()) element.RemoveNodes(); // normalize empty tag spelling
        }
    }

    internal static bool SameStructure1277(XElement? left, XElement? right)
        => StringComparer.Ordinal.Equals(StructuralXml1277(left), StructuralXml1277(right));

    private static bool ObserveInventories(XElement root)
    {
        if (root.Name != "character" || root.HasAttributes) return false;
        foreach (var (name, entry) in inventories)
            if (!TryContainer(root, name, entry, out _)) return false;
        // Installed ware remains unsupported. Other original owners require the
        // independent current-source read in TryPrepare; syntax is not authority.
        return Empty(root, "cyberwares");
    }

    internal bool TryInventoryDigest1253(XElement compiled, out string? digest)
    {
        digest = null;
        if (originalInventory3475 is not null && !originalInventory3475.Matches(compiled)) return false;
        foreach (var (name, entry) in inventories)
            if (!TryContainer(compiled, name, entry, out _)) return false;
        foreach (string name in new[] { "cyberwares", "vehicles" })
            if (!SameStructure1277(original.Element(name), compiled.Element(name))) return false;
        digest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "original-owned-inventory-and-selected-projection1253",
            Original = GraphDigest(original), Lines = linesDigest,
            Inventories = inventories.Select(pair => new { pair.Container,
                Original = StructuralXml1277(original.Element(pair.Container)),
                Compiled = StructuralXml1277(compiled.Element(pair.Container)) }).ToArray()
        });
        if (originalInventory3475 is not null)
            digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "unchanged-original-owner-source3475", Inventory = digest,
                OriginalOwnerSources = originalInventory3475.AuthorityDigest
            });
        return true;
    }

    internal static bool TryPrepare(XElement original, CharacterWorkspaceId owner,
        IReadOnlyList<CharacterCreationGearLine> lines, out CharacterCreationOwnedGearGraph1233? verified,
        ICharacterSourceDataResolver? originalSources3475 = null,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
    {
        verified = null;
        try
        {
            if (string.IsNullOrWhiteSpace(owner.Value) || !ObserveInventories(original)
                || !CharacterCreationFocusSelectionRules.TryValidateBasket(lines, out _)) return false;
            var preserved = lines.FirstOrDefault(l => l.Focus is not null)?.Focus?.Preserved;
            if (lines.Concat(preserved?.Lines ?? []).Where(l => l.Focus is not null)
                .Any(l => l.Focus!.Selection.OwnerWorkspaceId != owner)) return false;
            CharacterCreationOriginalInventory3475? inventory3475 = null;
            bool needsOriginalRead3475 = !Empty(original, "vehicles") || !Empty(original, "armors")
                || !Empty(original, "weapons") || preserved is null && !Empty(original, "gears");
            if (needsOriginalRead3475 && !CharacterCreationOriginalInventory3475.TryCapture(
                original, originalSources3475, out inventory3475, priorityGrants)) return false;
            if (preserved is null && (!Empty(original, "foci") || !Empty(original, "stackedfoci"))) return false;
            if (preserved is not null && (preserved.Selection.OriginalGraphDigest != GraphDigest(original)
                || !VerifyOwned(original, preserved))) return false;
            var ids = new HashSet<Guid>();
            foreach (var node in original.Descendants("guid"))
                if (!Guid.TryParse(node.Value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
            foreach (var row in lines.Where(l => l.Focus is not null))
            {
                var f = row.Focus!.Selection;
                if (!ids.Add(f.InstanceId) || f.FocusId is { } id && !ids.Add(id)) return false;
            }
            foreach (var stack in lines.Where(l => l.Focus?.Selection.Stack is not null)
                .Select(l => l.Focus!.Selection.Stack!).DistinctBy(s => s.StackId))
                if (!ids.Add(stack.StackId) || !ids.Add(stack.OwnerInstanceId)) return false;
            verified = new(original, owner, lines, inventory3475);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { return false; }
    }

    internal static bool TryCapture(XElement original, CharacterWorkspaceId owner, CharacterCreationGearAuthority authority,
        out CharacterCreationOwnedGearSelection? selection)
    {
        selection = null;
        try
        {
            if (!CharacterCreationGearRules.IsValidAuthority(authority) || string.IsNullOrWhiteSpace(owner.Value)
                || !ObserveInventories(original) || !Empty(original, "vehicles")
                || !Empty(original, "armors") || !Empty(original, "weapons")) return false;
            var saved = original.Element("gears")?.Elements().ToArray() ?? [];
            var links = original.Element("foci")?.Elements().ToArray() ?? [];
            var stacks = original.Element("stackedfoci")?.Elements().ToArray() ?? [];
            if (saved.Length == 0) return links.Length == 0 && stacks.Length == 0;
            var ids = new HashSet<Guid>();
            foreach (var node in original.Descendants("guid"))
                if (!Guid.TryParse(node.Value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
            var items = new List<CharacterCreationOwnedGearItem>();
            var stackItems = new List<CharacterCreationOwnedFocusStack1253>();
            var owners = new HashSet<Guid>();
            foreach (var stack in stacks)
            {
                if (!TryContainer(stack, "gears", "gear", out var components)
                    || components.Length is < 2 or > 6
                    || !Guid.TryParse(stack.Element("guid")?.Value, out var stackId)
                    || !Guid.TryParse(stack.Element("gearid")?.Value, out var ownerId) || !owners.Add(ownerId)
                    || !bool.TryParse(stack.Element("bonded")?.Value, out bool bonded)
                    || saved.Where(g => g.Element("guid")?.Value == ownerId.ToString("D")).ToArray() is not [var aggregate]) return false;
                var componentIds = components.Select(g => Guid.Parse(g.Element("guid")!.Value)).ToArray();
                var relation = new CharacterCreationFocusStackSelection(stackId, ownerId, bonded, componentIds) { SelectionId = stackId };
                foreach (var gear in components)
                    if (!CaptureItem(gear, relation, out var item)) return false;
                    else items.Add(item!);
                stackItems.Add(new(stackId, ownerId, GraphDigest(aggregate), GraphDigest(stack), bonded, componentIds));
            }
            foreach (var gear in saved)
            {
                if (owners.Contains(Guid.Parse(gear.Element("guid")!.Value))) continue;
                if (!CaptureItem(gear, null, out var item)) return false;
                items.Add(item!);
            }
            var candidate = new CharacterCreationOwnedGearSelection(GraphDigest(original), items.ToArray())
                { Stacks = stacks.Length == 0 ? null : stackItems.ToArray() };
            if (!CharacterCreationFocusSelectionRules.TryProjectInventory(candidate, authority.Options,
                    authority.MaximumAvailability, authority.MaximumQuantityPerLine, out var projected)
                || !VerifyOwned(original, projected!)) return false;
            selection = candidate;
            return true;

            bool CaptureItem(XElement gear, CharacterCreationFocusStackSelection? stack, out CharacterCreationOwnedGearItem? item)
            {
                item = null;
                if (!Guid.TryParse(gear.Element("sourceid")?.Value, out var sourceId)
                    || !Guid.TryParse(gear.Element("guid")?.Value, out var instance)
                    || !TryNativeQuantity1277(gear.Element("qty")?.Value, out int quantity)) return false;
                var source = authority.Options.SingleOrDefault(o => o.SourceId == sourceId);
                if (source is null || !source.IsSelectable) return false;
                CharacterCreationFocusSelection? focus = null;
                if (source.Category == "Foci")
                {
                    if (source.FocusSource is not { } definition
                        || !int.TryParse(gear.Element("rating")?.Value, out int rating)
                        || !bool.TryParse(gear.Element("bonded")?.Value, out bool bonded)) return false;
                    var ordinary = links.Where(n => n.Element("gearid")?.Value == instance.ToString("D")).ToArray();
                    Guid? focusId = null;
                    if (stack is null && bonded)
                    {
                        if (ordinary.Length != 1 || !Guid.TryParse(ordinary[0].Element("guid")?.Value, out var id)) return false;
                        focusId = id;
                    }
                    else if (ordinary.Length != 0) return false;
                    string extra = gear.Element("extra")?.Value ?? string.Empty;
                    Guid? extraSource = null;
                    if (definition.SelectTradition)
                    {
                        if (original.Elements("tradition").ToArray() is not [var tradition]
                            || tradition.Element("name")?.Value != extra
                            || !Guid.TryParse(tradition.Element("sourceid")?.Value, out var id)) return false;
                        extraSource = id;
                    }
                    focus = new(instance, focusId, source.SourceNodeDigest, rating, extra, extraSource,
                        gear.Element("grade")?.Value ?? string.Empty, bonded, owner, instance) { Stack = stack };
                }
                else if (stack is not null) return false;
                item = new(instance, source.OptionId, source.SourceNodeDigest, GraphDigest(gear), quantity, focus);
                return true;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { return false; }
    }

    private static bool VerifyOwned(XElement original, CharacterCreationOwnedGearProjection preserved)
    {
        var direct = original.Element("gears")?.Elements().ToArray() ?? [];
        var stacks = original.Element("stackedfoci")?.Elements().ToArray() ?? [];
        var links = original.Element("foci")?.Elements().ToArray() ?? [];
        var owners = preserved.Selection.Stacks ?? [];
        if (owners.Count != stacks.Length || preserved.Lines.Count != preserved.Selection.Items.Count) return false;
        var inventory = direct.Where(g => !owners.Any(s => s.OwnerInstanceId.ToString("D") == g.Element("guid")?.Value))
            .Concat(stacks.SelectMany(s => s.Element("gears")?.Elements() ?? [])).ToArray();
        if (inventory.Length != preserved.Selection.Items.Count) return false;
        var expectedLinks = new List<XElement>();
        for (int i = 0; i < preserved.Selection.Items.Count; i++)
        {
            var item = preserved.Selection.Items[i]; var line = preserved.Lines[i];
            if (inventory.Where(n => n.Element("guid")?.Value == item.InstanceId.ToString("D")).ToArray() is not [var actual]
                || item.SavedGearDigest != GraphDigest(actual)
                || !CharacterCreationLegacySourceProjector.TryBuildGear(line, preserved.Selection.OriginalGraphDigest, out var expected)) return false;
            expected.SetElementValue("guid", item.InstanceId.ToString("D"));
            if (!SameOwnedShape(actual, expected)) return false;
            if (item.Focus is { Bonded: true, Stack: null } focus) expectedLinks.Add(Focus(focus));
        }
        if (expectedLinks.Count != links.Length || !expectedLinks.All(e => links.Any(f => SameStructure1277(e, f)))) return false;
        foreach (var owner in owners)
        {
            if (stacks.Where(s => s.Element("guid")?.Value == owner.StackId.ToString("D")).ToArray() is not [var stack]
                || direct.Where(g => g.Element("guid")?.Value == owner.OwnerInstanceId.ToString("D")).ToArray() is not [var aggregate]
                || GraphDigest(stack) != owner.SavedStackDigest || GraphDigest(aggregate) != owner.SavedOwnerDigest
                || stack.Element("gearid")?.Value != owner.OwnerInstanceId.ToString("D")
                || !bool.TryParse(stack.Element("bonded")?.Value, out bool bonded) || bonded != owner.Bonded
                || !owner.ComponentInstanceIds.SequenceEqual(stack.Element("gears")!.Elements().Select(g => Guid.Parse(g.Element("guid")!.Value)))) return false;
            var expectedStack = new XElement("stackedfocus", new XElement("guid", owner.StackId.ToString("D")),
                new XElement("gearid", owner.OwnerInstanceId.ToString("D")), new XElement("bonded", bonded ? "True" : "False"),
                new XElement("gears", stack.Element("gears")!.Elements().Select(g => new XElement(g))));
            if (!SameStructure1277(stack, expectedStack)) return false;
            string name = aggregate.Element("name")?.Value ?? string.Empty;
            decimal cost = preserved.Lines.Where(l => l.Focus?.Selection.Stack?.StackId == owner.StackId).Sum(l => l.TotalCost);
            var expectedOwner = CharacterCreationLegacySourceProjector.BuildStackOwner1253(owner.OwnerInstanceId, name, cost);
            if (!name.StartsWith("Stacked Focus: ", StringComparison.Ordinal) || !SameOwnedShape(aggregate, expectedOwner)) return false;
        }
        return true;
    }

    internal bool RetainedNodesMatch1253(XElement graph)
        => (originalInventory3475 is null || originalInventory3475.Matches(graph))
            && retained.All(n => graph.Element("gears")?.Elements().Any(v => SameStructure1277(v, n)) == true)
            && retainedFoci.All(n => graph.Element("foci")?.Elements().Any(v => SameStructure1277(v, n)) == true)
            && retainedStacks.All(n => graph.Element("stackedfoci")?.Elements().Any(v => SameStructure1277(v, n)) == true);

    internal static bool Empty(XElement root, string name)
        => root.Elements().Where(n => n.Name.LocalName == name).All(n => n.Name == name && !n.HasAttributes
            && !n.HasElements && n.Nodes().All(v => v.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(((XText)v).Value)))
            && root.Elements(name).Count() <= 1;

    internal static bool Merge(XElement root, XElement purchases, IReadOnlyList<CharacterCreationGearLine> lines,
        CharacterCreationOwnedGearGraph1233? verified)
    {
        nativeStacks1351.Remove(root);
        if (verified is null)
        {
            if (lines.Any(l => l.Focus is not null)) return false;
            Replace(root, purchases); return true;
        }
        if (verified.linesDigest != CharacterCreationFinalizationDigest.Compute(lines)) return false;
        var gears = new XElement("gears", verified.retained.Select(n => new XElement(n)), purchases.Elements().Select(n => new XElement(n)));
        var foci = new XElement("foci", verified.retainedFoci.Select(n => new XElement(n)),
            lines.Where(l => l.Focus?.Selection is { Bonded: true, Stack: null }).Select(l => Focus(l.Focus!.Selection)));
        var stacks = new XElement("stackedfoci", verified.retainedStacks.Select(n => new XElement(n)));
        foreach (var group in lines.Where(l => l.Focus?.Selection.Stack is not null).GroupBy(l => l.Focus!.Selection.Stack!.StackId))
        {
            var relation = group.First().Focus!.Selection.Stack!;
            var components = relation.ComponentInstanceIds.Select(id => gears.Elements().Single(g => g.Element("guid")?.Value == id.ToString("D"))).ToArray();
            var stack = new XElement("stackedfocus", new XElement("guid", relation.StackId.ToString("D")),
                new XElement("gearid", relation.OwnerInstanceId.ToString("D")), new XElement("bonded", relation.Bonded ? "True" : "False"),
                new XElement("gears", components.Select(g => new XElement(g))));
            foreach (var component in components) component.Remove();
            gears.Add(CharacterCreationLegacySourceProjector.BuildStackOwner1253(relation.OwnerInstanceId,
                "Stacked Focus: " + string.Join(", ", components.Select(g => g.Element("name")!.Value)), group.Sum(l => l.TotalCost)));
            stacks.Add(stack);
        }
        var ids = gears.Descendants("guid").Concat(foci.Descendants("guid")).Concat(stacks.Descendants("guid")).Select(n => n.Value).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) return false;
        Replace(root, gears);
        if (foci.HasElements || root.Element("foci") is not null) Replace(root, foci);
        if (stacks.HasElements || root.Element("stackedfoci") is not null) Replace(root, stacks);
        return !stacks.HasElements || BindNativeStacks1351(root, lines, verified);
    }

    // Producer custody is never serialized or inferred from Guid.Empty/category.
    // Only a successful native merge from TryPrepare can bind these occurrences.
    private sealed record NativeStackNode1351(XElement Node, XElement? Parent, string Xml);
    private sealed record NativeStackGraph1351(CharacterCreationOwnedGearGraph1233 Verified,
        Guid[] Owners, XElement[] Stacks, NativeStackNode1351[] Nodes);
    private static readonly ConditionalWeakTable<XElement, NativeStackGraph1351> nativeStacks1351 = new();

    private static bool BindNativeStacks1351(XElement root, IReadOnlyList<CharacterCreationGearLine> lines,
        CharacterCreationOwnedGearGraph1233 verified)
    {
        if (!TryContainer(root, "gears", "gear", out var gears)
            || !TryContainer(root, "stackedfoci", "stackedfocus", out var stacks)
            || !verified.RetainedNodesMatch1253(root)) return false;
        var groups = lines.Where(l => l.Focus?.Selection.Stack is not null)
            .GroupBy(l => l.Focus!.Selection.Stack!.StackId).ToArray();
        if (stacks.Length != verified.retainedStacks.Length + groups.Length) return false;
        foreach (var group in groups)
        {
            var relation = group.First().Focus!.Selection.Stack!;
            if (stacks.Where(s => s.Element("guid")?.Value == relation.StackId.ToString("D")).ToArray() is not [var stack]
                || gears.Where(g => g.Element("guid")?.Value == relation.OwnerInstanceId.ToString("D")).ToArray() is not [var aggregate]
                || !TryContainer(stack, "gears", "gear", out var components)
                || components.Length != relation.ComponentInstanceIds.Count) return false;
            for (int i = 0; i < components.Length; i++)
            {
                Guid id = relation.ComponentInstanceIds[i];
                if (components[i].Element("guid")?.Value != id.ToString("D")
                    || group.Where(l => l.Focus!.Selection.InstanceId == id).ToArray() is not [var line]
                    || !CharacterCreationLegacySourceProjector.TryBuildFocusGear1233(line, out var expected)
                    || !SameOwnedShape(components[i], expected)) return false;
            }
            var expectedStack = new XElement("stackedfocus", new XElement("guid", relation.StackId.ToString("D")),
                new XElement("gearid", relation.OwnerInstanceId.ToString("D")),
                new XElement("bonded", relation.Bonded ? "True" : "False"),
                new XElement("gears", components.Select(g => new XElement(g))));
            var expectedOwner = CharacterCreationLegacySourceProjector.BuildStackOwner1253(relation.OwnerInstanceId,
                "Stacked Focus: " + string.Join(", ", components.Select(g => g.Element("name")!.Value)), group.Sum(l => l.TotalCost));
            if (!SameStructure1277(stack, expectedStack) || !SameOwnedShape(aggregate, expectedOwner)) return false;
        }
        var ids = new HashSet<Guid>();
        foreach (var node in root.Descendants("guid"))
            if (!Guid.TryParse(node.Value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
        var owners = stacks.Select(s => Guid.Parse(s.Element("gearid")!.Value)).ToArray();
        if (owners.Distinct().Count() != owners.Length) return false;
        var aggregates = owners.Select(id => gears.Single(g => g.Element("guid")?.Value == id.ToString("D"))).ToArray();
        // Retained stacks were source/owner/shape-validated by VerifyOwned during
        // TryPrepare. New stacks above use that same native projector/shape check.
        var nodes = stacks.Concat(aggregates).SelectMany(n => n.DescendantsAndSelf())
            .Select(n => new NativeStackNode1351(n, n.Parent, n.ToString(SaveOptions.DisableFormatting))).ToArray();
        nativeStacks1351.Add(root, new(verified, owners, stacks, nodes));
        return true;
    }

    internal static bool TryReadNativeStackOwners1351(XElement root, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, out Guid[] owners)
    {
        owners = [];
        if (!nativeStacks1351.TryGetValue(root, out var binding))
            return Empty(root, "stackedfoci") && !root.Descendants().Any(n => n.Name.LocalName == "stackedfocus");
        var gear = inputs.Lifestyles.Gear;
        if (inputs.OriginalCharacter is not { } original || binding.Verified.owner1351 != original.WorkspaceId
            || binding.Verified.original.ToString(SaveOptions.DisableFormatting)
                != XElement.Parse(original.RawCharacterXml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting)
            || !CharacterCreationGearRules.TryProjectBasket(request.Selections.Gear, gear.State.Authority,
                gear.State.Budget.TotalStartingNuyen, out var lines, out _, out _)
            || binding.Verified.linesDigest != CharacterCreationFinalizationDigest.Compute(lines)
            || !ValidateExtras(root, lines)
            || !TryContainer(root, "stackedfoci", "stackedfocus", out var stacks)
            || !stacks.SequenceEqual(binding.Stacks, ReferenceEqualityComparer.Instance)
            || root.Descendants().Count(n => n.Name.LocalName == "stackedfocus") != stacks.Length
            || binding.Nodes.Any(n => !ReferenceEquals(n.Node.Parent, n.Parent)
                || !ReferenceEquals(n.Node.Ancestors().LastOrDefault(), root)
                || n.Xml != n.Node.ToString(SaveOptions.DisableFormatting))) return false;
        var ids = new HashSet<Guid>();
        foreach (var node in root.Descendants("guid"))
            if (!Guid.TryParse(node.Value, out var id) || id == Guid.Empty || !ids.Add(id)) return false;
        owners = binding.Owners.ToArray();
        return true;
    }

    internal static bool ValidateExtras(XElement graph, IReadOnlyList<CharacterCreationGearLine> lines)
    {
        var preserved = lines.FirstOrDefault(l => l.Focus is not null)?.Focus?.Preserved;
        foreach (var row in lines.Concat(preserved?.Lines ?? []).Where(l => l.Focus is not null))
        {
            var focus = row.Focus!.Selection;
            foreach (var identity in new Guid?[] { focus.InstanceId, focus.FocusId }.Where(id => id.HasValue))
                if (graph.Descendants("guid").Count(n => Guid.TryParse(n.Value, out var nativeId) && nativeId == identity) != 1) return false;
            if (!CharacterCreationFocusSelectionRules.TryReadSource(row.SourceNodeXml, out var source)) return false;
            if (source!.RequiredQuality1253 is { } required
                && !graph.Elements("qualities").SelectMany(n => n.Elements("quality")).Any(q =>
                    q.Elements("name").ToArray() is [var qualityName] && qualityName.Value == required
                    && q.Elements("sourceid").ToArray() is [var sourceId]
                    && Guid.TryParse(sourceId.Value, out var canonical) && canonical != Guid.Empty)) return false;
            if (source!.SelectTradition && (graph.Elements("tradition").ToArray() is not [var tradition]
                || tradition.Elements("sourceid").ToArray() is not [var traditionSource]
                || !Guid.TryParse(traditionSource.Value, out var traditionId) || focus.ExtraSourceId != traditionId
                || tradition.Elements("name").ToArray() is not [var name] || name.Value != focus.Extra)) return false;
        }
        return true;
    }

    private static XElement Focus(CharacterCreationFocusSelection focus)
        => new("focus", new XElement("guid", focus.FocusId!.Value.ToString("D")), new XElement("gearid", focus.InstanceId.ToString("D")));

    private static bool SameOwnedShape(XElement actual, XElement expected)
    {
        var a = new XElement(actual); var e = new XElement(expected);
        // Gear.Save writes decimal quantities (the native default is 1.0m).
        // Admit only integral quantities supported by this creation contract.
        if (a.Elements("qty").ToArray() is not [var quantity] || quantity.HasAttributes || quantity.HasElements
            || !TryNativeQuantity1277(quantity.Value, out int count)
            || e.Element("qty")?.Value != count.ToString(CultureInfo.InvariantCulture)) return false;
        quantity.Value = count.ToString(CultureInfo.InvariantCulture);
        foreach (string field in new[] { "notes", "notesColor", "gearname", "location", "sortorder", "equipped", "wirelesson" })
        {
            if (a.Elements(field).ToArray() is not [var node] || node.HasAttributes || node.HasElements) return false;
            if (field is "equipped" or "wirelesson" && !bool.TryParse(node.Value, out _)) return false;
            if (field == "sortorder" && !int.TryParse(node.Value, out _)) return false;
            a.Element(field)!.Remove(); e.Element(field)?.Remove();
        }
        return SameStructure1277(a, e);
    }

    private static bool TryNativeQuantity1277(string? value, out int quantity)
    {
        quantity = 0;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var native)
            || native < 1 || native > int.MaxValue || decimal.Truncate(native) != native) return false;
        quantity = (int)native;
        return true;
    }

    private static bool TryContainer(XElement root, string name, string child, out XElement[] rows)
    {
        rows = [];
        var containers = root.Elements().Where(n => n.Name.LocalName == name).ToArray();
        if (containers.Length == 0) return true;
        if (containers is not [var container] || container.Name != name || container.HasAttributes
            || container.Nodes().Any(n => n is not XElement && (n.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)n).Value)))
            || container.Elements().Any(n => n.Name != child || n.HasAttributes)) return false;
        rows = container.Elements().ToArray(); return true;
    }

    private static void Replace(XElement root, XElement node)
    {
        if (root.Element(node.Name) is { } existing) existing.ReplaceWith(node); else root.Add(node);
    }
}
