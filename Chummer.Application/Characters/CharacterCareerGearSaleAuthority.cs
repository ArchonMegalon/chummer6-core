using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Shared typed disposal authority. Remove and Sale have distinct quotes and bookkeeping; neither refunds a purchase.
public static class CharacterCareerGearSaleAuthority
{
    public const decimal MinimumResalePercent = 0m;
    public const decimal MaximumResalePercent = 9999.99m; // SellItem.Designer.cs decimal bits 999999, scale 2.
    public const int ResalePercentDecimalPlaces = 2;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool IsRequest(CharacterCareerGearSaleRequest? r) => r is not null
        && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(r.WorkspaceId)
        && r.OperationId != Guid.Empty && r.InstanceId != Guid.Empty && r.OperationId != r.InstanceId
        && CharacterCareerGearSaleTransaction.IsDigest(r.ExpectedNodeRevision)
        && (r.OperationKind switch
        {
            CharacterCareerGearOperationKind.Sale => r.ExpenseId != Guid.Empty
                && r.ExpenseId != r.OperationId && r.ExpenseId != r.InstanceId
                && r.ResalePercent >= MinimumResalePercent && r.ResalePercent <= MaximumResalePercent
                && r.ResalePercent % 0.01m == 0m && r.ExpenseDate.Offset == TimeSpan.Zero
                && r.ExpenseDate.Ticks % TimeSpan.TicksPerSecond == 0,
            CharacterCareerGearOperationKind.Remove => r.ResalePercent == 0m && r.ExpenseId == Guid.Empty
                && r.ExpenseDate == default && r.ExpenseDate.Offset == TimeSpan.Zero,
            _ => false
        });

    internal static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var document = XDocument.Load(reader);
        if (document.Root is not { } root || root.Name != "character" || root.HasAttributes
            || root.Elements("created").ToArray() is not [var created] || !bool.TryParse(created.Value, out bool career) || !career
            || root.Elements("gears").ToArray() is not [var gears] || gears.HasAttributes
            || root.Elements("nuyen").ToArray() is not [var nuyen] || nuyen.HasAttributes || nuyen.HasElements
            || !decimal.TryParse(nuyen.Value, NumberStyles.Number, Invariant, out decimal balance) || balance < 0m
            || root.Elements("expenses").Count() > 1 || root.Element("expenses")?.HasAttributes == true)
            throw new InvalidOperationException("gear_sale_character_shape_unavailable");
        return document;
    }

    public static IReadOnlyList<CharacterCareerGearSaleNode> ReadInstalled(string xml, ICharacterSourceDataResolver resolver)
    {
        var root = Parse(xml).Root!;
        var context = resolver.TryCreateContext(xml);
        if (context is null || !context.TryResolveCareerSettingsProfile(out string settings, out string profile)
            || string.IsNullOrWhiteSpace(settings) || string.IsNullOrWhiteSpace(profile))
            throw new InvalidOperationException("gear_sale_source_profile_unavailable");
        decimal nuyen = decimal.Parse(root.Element("nuyen")!.Value, Invariant);
        var result = new List<CharacterCareerGearSaleNode>();
        // Include unsupported nested shapes as explicit unavailable rows; never silently flatten them into top-level authority.
        foreach (var gear in root.Descendants("gear"))
        {
            var blockers = new List<string>();
            Guid? id = Guid.TryParseExact((string?)gear.Element("guid"), "D", out var parsed) && parsed != Guid.Empty ? parsed : null;
            string installed = gear.ToString(SaveOptions.DisableFormatting);
            string name = (string?)gear.Element("name") ?? "Gear";
            string? dependency = ProveNoDeletionDependencies(root, gear, id);
            if (dependency is not null) blockers.Add(dependency);
            CharacterGearCostSnapshot? cost = null;
            if (!Guid.TryParseExact((string?)gear.Element("sourceid"), "D", out var sourceId)
                || !context.TryResolveCareerGearPurchaseSource(sourceId, out var source) || source is null)
                blockers.Add("gear_sale_source_unavailable");
            else if (!CharacterCreationLegacySourceProjector.TryReadCareerSaleShape(source, gear, out cost, out var error))
                blockers.Add(error.Length == 0 ? "gear_sale_saved_shape_unsupported" : error);
            string? removalError = ProveRemovalTree(root, gear, id, out var removalNodes);
            if (removalError is null)
                foreach (var node in removalNodes!)
                {
                    if (!Guid.TryParseExact((string?)node.Element("sourceid"), "D", out var removalSourceId)
                        || !context.TryResolveCareerGearPurchaseSource(removalSourceId, out var removalSource) || removalSource is null)
                        removalError = "gear_remove_source_unavailable";
                    else if (!CharacterCreationLegacySourceProjector.TryReadCareerRemovalShape(removalSource, node, out var shapeError))
                        removalError = "gear_remove_shape:" + shapeError;
                    if (removalError is not null) break;
                }
            result.Add(new(id, name, CharacterCareerGearSaleTransaction.Hash(installed), installed, cost, nuyen, blockers.ToArray())
            { RemovalBlockers = removalError is null ? Array.Empty<string>() : new[] { removalError } });
            if (result.Count > 4096) throw new InvalidOperationException("gear_sale_installed_limit");
        }
        return Array.AsReadOnly(result.ToArray());
    }

    // Gear.DeleteGear: no children, generated weapons, Gear improvements, focus/stacked-focus,
    // loaded clip or active-commlink reference may survive this proof. A source row alone cannot prove that.
    private static string? ProveNoDeletionDependencies(XElement root, XElement gear, Guid? id, bool provenTreeMember = false)
    {
        if (id is null) return "gear_sale_identity_unavailable";
        if (!provenTreeMember && gear.Parent != root.Element("gears")) return "gear_sale_parented_unsupported";
        if (!provenTreeMember && (gear.Descendants("gear").Any() || gear.Element("children")?.HasElements == true))
            return "gear_sale_children_unsupported";
        if ((string?)gear.Element("category") is "Foci" or "Metamagic Foci" or "Stacked Focus")
            return "gear_sale_focus_unsupported";
        foreach (string field in new[] { "parentid", "weaponid", "weaponguid", "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
            if (gear.Elements(field).Any(e => e.HasElements || !string.IsNullOrWhiteSpace(e.Value)))
                return "gear_sale_deletion_dependency:" + field;
        foreach (string field in new[] { "active", "homenode", "bonded", "wirelesson" })
            if (gear.Element(field) is not { } value || !bool.TryParse(value.Value, out bool enabled) || enabled)
                return "gear_sale_deletion_dependency:" + field;
        var ownGuid = gear.Element("guid");
        if (root.Descendants("guid").Count(e => Guid.TryParse(e.Value, out var other) && other == id.Value) != 1)
            return "gear_sale_duplicate_identity";
        // All serialized reference locations are inspected, including improvements, foci,
        // stacked foci, clip ammo and generated weapon parent IDs. Also reject composite refs.
        string canonical = id.Value.ToString("D"), compact = id.Value.ToString("N");
        bool Ref(string value) => value.Contains(canonical, StringComparison.OrdinalIgnoreCase)
            || value.Contains(compact, StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(value, out var other) && other == id.Value;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element == ownGuid) continue;
            // Gear.DeleteGear does not mutate historical expense undo records. This exact
            // AddGear pointer is inert sale history and must survive the sale unchanged.
            if (element.Name == "objectid" && element.Parent is { Name.LocalName: "undo" } undo
                && undo.Parent is { Name.LocalName: "expense" } expense && expense.Parent == root.Element("expenses")
                && !element.HasElements && !element.HasAttributes && undo.Elements().Count() == 5
                && undo.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                && undo.Elements().All(e => !e.HasElements && !e.HasAttributes)
                && (string?)undo.Element("karmatype") == "ImproveAttribute"
                && (string?)undo.Element("nuyentype") == "AddGear" && (string?)undo.Element("extra") == ""
                && decimal.TryParse((string?)undo.Element("qty"), NumberStyles.Number, Invariant, out decimal oldQuantity) && oldQuantity > 0m)
                continue;
            if ((!element.HasElements && Ref(element.Value)) || element.Attributes().Any(a => Ref(a.Value)))
                return "gear_sale_external_reference:" + element.Name.LocalName;
        }
        return null;
    }

    // Gear.Save writes child Gear under <children>; DeleteGear recursively deletes those
    // children with blnDoRemoval=false. This bounded subset admits complete ordinary trees,
    // not independently selected parented items or source-generated/included children.
    private const int MaximumRemovalNodes = 128;
    private const int MaximumRemovalDepth = 16;
    private static string? ProveRemovalTree(XElement root, XElement gear, Guid? id,
        out IReadOnlyList<XElement>? nodes)
    {
        nodes = null;
        if (gear.Parent != root.Element("gears")) return "gear_remove_parented_unsupported";
        var result = new List<XElement>();
        var pending = new Stack<(XElement Node, int Depth)>();
        pending.Push((gear, 0));
        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            if (depth > MaximumRemovalDepth || result.Count >= MaximumRemovalNodes)
                return "gear_remove_tree_limit";
            if (node.Name != "gear" || node.HasAttributes
                || node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                || node.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
                || node.Elements().Any(e => e.HasAttributes || e.Name != "children" && e.HasElements)
                || node.Elements("children").ToArray() is not [var children]
                || children.HasAttributes || children.Elements().Any(e => e.Name != "gear")
                || children.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                || !Guid.TryParseExact((string?)node.Element("guid"), "D", out var childId) || childId == Guid.Empty
                || !decimal.TryParse((string?)node.Element("qty"), NumberStyles.AllowDecimalPoint, Invariant, out var quantity)
                || !CharacterGearQuantityRules.IsValidAmount(quantity, 1m)) return "gear_remove_tree_shape_unsupported";
            if (node == gear && id != childId) return "gear_remove_identity_unavailable";
            var dependency = ProveNoDeletionDependencies(root, node, childId, provenTreeMember: true);
            if (dependency is not null) return "gear_remove_dependency:" + dependency;
            result.Add(node);
            // Reverse the stack input to retain saved XML preorder in the immutable quote.
            foreach (var child in children.Elements("gear").Reverse()) pending.Push((child, depth + 1));
        }
        nodes = result.AsReadOnly();
        return null;
    }

    // Cold history verifies the immutable saved tree without current source availability.
    // Source-backed scalar admission and all external references are rechecked by the store
    // before dispatch and again at the serialized final replacement fence.
    private static bool TryRemovalEvidence(CharacterCareerGearSaleNode item, out IReadOnlyList<CharacterCareerGearRemovalItem>? items)
    {
        items = null;
        if (item.InstalledXml.Length > 1_048_576) return false;
        using var reader = XmlReader.Create(new StringReader(item.InstalledXml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
        var gear = XElement.Load(reader);
        var root = new XElement("character", new XElement("gears", gear));
        if ((string?)gear.Element("name") != item.Name
            || ProveRemovalTree(root, gear, item.InstanceId, out var nodes) is not null) return false;
        items = Array.AsReadOnly(nodes!.Select(n => new CharacterCareerGearRemovalItem(
            Guid.Parse(n.Element("guid")!.Value), (string?)n.Element("name") ?? "Gear",
            decimal.Parse(n.Element("qty")!.Value, Invariant),
            CharacterCareerGearSaleTransaction.Hash(n.ToString(SaveOptions.DisableFormatting)))).ToArray());
        return true;
    }

    public static bool TryQuote(CharacterCareerGearSaleNode item, CharacterCareerGearSaleRequest request,
        out CharacterCareerGearSaleQuote? quote)
    {
        quote = null;
        try
        {
            if (!IsRequest(request) || item.InstanceId != request.InstanceId
                || item.NodeRevision != request.ExpectedNodeRevision
                || item.NodeRevision != CharacterCareerGearSaleTransaction.Hash(item.InstalledXml)) return false;
            if (request.OperationKind == CharacterCareerGearOperationKind.Remove)
            {
                if (item.RemovalBlockers is not { Count: 0 } || item.Nuyen < 0m
                    || !TryRemovalEvidence(item, out var removed)) return false;
                // Gear.Remove ignores DeleteGear's return value. No price evaluation, balance
                // normalization, expense, refund, or sale proceeds belong to this operation.
                quote = new(item, 0m, 0m, 0m, item.Nuyen, "") { RemovedItems = removed };
                return true;
            }
            if (item.Blockers is not { Count: 0 } || !TrySavedCost(item, out var cost)
                || !CharacterGearQuantityRules.TryCalculatePurchaseTotalCost(cost!, out decimal total)) return false;
            // Gear.Sell top-level branch: (DeleteGear() + TotalCost) * selected fraction.
            // The independent dependency proof admits exactly zero deletion proceeds.
            decimal proceeds = checked((0m + total) * (request.ResalePercent / 100m));
            decimal after = checked(item.Nuyen + proceeds);
            if (item.Nuyen < 0m || after < 0m) return false;
            var expense = new XElement("expense", new XElement("guid", request.ExpenseId.ToString("D")),
                new XElement("date", request.ExpenseDate.UtcDateTime.ToString("s", Invariant)),
                new XElement("amount", proceeds.ToString(Invariant)), new XElement("reason", "Sold Gear " + item.Name),
                new XElement("type", "Nuyen"), new XElement("refund", "False"), new XElement("forcecareervisible", "False"));
            // Retain the exact displayed node, including any removal-capability witness.
            // Existing native Sale callers compare the complete catalog node to this quote.
            quote = new(item, total, 0m, proceeds, after, expense.ToString(SaveOptions.DisableFormatting));
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException or OverflowException or FormatException) { return false; }
    }

    // Cold receipt verification uses saved evidence only; it must not consult a changed/missing source catalog.
    private static bool TrySavedCost(CharacterCareerGearSaleNode item, out CharacterGearCostSnapshot? cost)
    {
        cost = null;
        if (item.Cost is not { Children.Count: 0 } saved || item.InstalledXml.Length > 1_048_576) return false;
        using var reader = XmlReader.Create(new StringReader(item.InstalledXml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
        var gear = XElement.Load(reader);
        if (gear.Name != "gear" || gear.HasAttributes || gear.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || gear.Elements().Any(e => e.HasAttributes || e.HasElements)
            || !Guid.TryParse((string?)gear.Element("guid"), out var id) || id != item.InstanceId
            || (string?)gear.Element("name") != item.Name
            || !int.TryParse((string?)gear.Element("rating"), NumberStyles.None, Invariant, out int rating)
            || !decimal.TryParse((string?)gear.Element("qty"), NumberStyles.AllowDecimalPoint, Invariant, out decimal qty)
            || !decimal.TryParse((string?)gear.Element("costfor") ?? "1", NumberStyles.AllowDecimalPoint, Invariant, out decimal divisor)
            || !bool.TryParse((string?)gear.Element("discountedcost"), out bool discount)
            || !int.TryParse((string?)gear.Element("childcostmultiplier") ?? "1", NumberStyles.None, Invariant, out int multiplier)) return false;
        cost = new(rating, qty, (string?)gear.Element("cost") ?? "", divisor, discount, multiplier, []);
        return CharacterCareerGearSaleTransaction.Same(cost, saved) && CharacterGearQuantityRules.IsValidAmount(qty, 1m);
    }

    public static bool IsQuote(CharacterCareerGearSaleRequest request, CharacterCareerGearSaleQuote? quote)
        => quote is { Item: not null } && TryQuote(quote.Item, request, out var expected)
            && CharacterCareerGearSaleTransaction.Same(quote, expected);

    internal static string Materialize(string xml, CharacterCareerGearSaleCommand command)
    {
        if (!IsQuote(command.Request, command.Quote)) throw new InvalidOperationException("gear_sale_quote_invalid");
        var document = Parse(xml);
        var root = document.Root!;
        var request = command.Request;
        var item = root.Element("gears")!.Elements("gear")
            .Single(e => Guid.TryParse((string?)e.Element("guid"), out var id) && id == request.InstanceId);
        if (item.ToString(SaveOptions.DisableFormatting) != command.Quote.Item.InstalledXml
            || (request.OperationKind == CharacterCareerGearOperationKind.Remove
                ? ProveRemovalTree(root, item, request.InstanceId, out _) : ProveNoDeletionDependencies(root, item, request.InstanceId)) is not null
            || decimal.Parse(root.Element("nuyen")!.Value, Invariant) != command.Quote.Item.Nuyen
            || root.Descendants().Where(e => !e.HasElements).Any(e => Guid.TryParse(e.Value, out var id)
                && (id == request.OperationId || request.OperationKind == CharacterCareerGearOperationKind.Sale && id == request.ExpenseId)))
            throw new InvalidOperationException("gear_sale_identity_or_dependency_changed");
        // Only after both source-shape admission and independent deletion proof; no generic XML-delete API.
        item.Remove();
        if (request.OperationKind == CharacterCareerGearOperationKind.Sale)
        {
            root.Element("nuyen")!.Value = command.Quote.NuyenAfter.ToString(Invariant);
            if (root.Element("expenses") is not { } expenses) { expenses = new XElement("expenses"); root.Add(expenses); }
            expenses.Add(XElement.Parse(command.Quote.ExpenseXml));
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
