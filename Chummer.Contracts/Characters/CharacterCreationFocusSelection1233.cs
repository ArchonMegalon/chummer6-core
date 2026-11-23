using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

// Optional fields on existing records are omitted when absent, preserving V1 hashes.
public sealed record CharacterCreationFocusSelection(
    [property: JsonRequired] Guid InstanceId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? FocusId,
    [property: JsonRequired] string SourceNodeDigest,
    [property: JsonRequired] int Rating,
    [property: JsonRequired] string Extra,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? ExtraSourceId,
    [property: JsonRequired] string Grade,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool Bonded,
    [property: JsonRequired] CharacterWorkspaceId OwnerWorkspaceId,
    [property: JsonRequired] Guid SelectionId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationFocusStackSelection? Stack { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationOwnedGearSelection? Preserve { get; init; }
}

// Components retain their canonical source IDs. The derived owner has no catalogue ID.
public sealed record CharacterCreationFocusStackSelection(Guid StackId, Guid OwnerInstanceId,
    bool Bonded, IReadOnlyList<Guid> ComponentInstanceIds)
{
    [JsonRequired] public Guid SelectionId { get; init; }
}

public sealed record CharacterCreationOwnedGearSelection(string OriginalGraphDigest,
    IReadOnlyList<CharacterCreationOwnedGearItem> Items)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationOwnedFocusStack1253>? Stacks { get; init; }
}

public sealed record CharacterCreationOwnedFocusStack1253(Guid StackId, Guid OwnerInstanceId,
    string SavedOwnerDigest, string SavedStackDigest, bool Bonded, IReadOnlyList<Guid> ComponentInstanceIds);

public sealed record CharacterCreationOwnedGearItem(Guid InstanceId, string OptionId,
    string SourceNodeDigest, string SavedGearDigest, int Quantity,
    CharacterCreationFocusSelection? Focus);

public sealed record CharacterCreationOwnedGearProjection(CharacterCreationOwnedGearSelection Selection,
    IReadOnlyList<CharacterCreationGearLine> Lines, decimal TotalCost);

public sealed record CharacterCreationFocusProjection(CharacterCreationFocusSelection Selection,
    CharacterCreationOwnedGearProjection? Preserved);

public sealed record CharacterCreationFocusSource(Guid SourceId, string Name, string Category,
    int MinimumRating, int MaximumRating, string CostExpression, string AvailabilityExpression,
    string SourceBook, string Page, bool SelectTradition)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredQuality1253 { get; init; }
}

/// <summary>Bounded source grammar, not focus Karma arithmetic or save authority.</summary>
public static class CharacterCreationFocusSelectionRules
{
    public const string Invalid = "creation-focus-selection-invalid";
    public const string OwnedInvalid = "creation-focus-owned-inventory-invalid";
    public const string StackUnsupported = "creation-focus-stack-source-owner-unsupported";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "id", "name", "category", "rating", "minrating", "ratinglabel", "source", "page",
        "avail", "cost", "bonus", "required", "translate", "altpage", "notes", "altnotes", "notesColor"
    };

    public static bool TryReadSource(string xml,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CharacterCreationFocusSource? definition)
    {
        definition = null;
        if (xml is not { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = CharacterCreationGearRules.MaximumSourceNodeLength });
            var source = XElement.Load(reader);
            if (source.Name != "gear" || source.HasAttributes
                || source.Nodes().Any(n => n is not XElement && (n is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
                || source.Elements().Any(n => n.Name.NamespaceName.Length != 0 || !Allowed.Contains(n.Name.LocalName))
                || source.Elements().GroupBy(n => n.Name).Any(g => g.Count() != 1)
                || source.Elements().Where(n => n.Name != "bonus" && n.Name != "required").Any(n => n.HasAttributes || n.HasElements)
                || !Guid.TryParse(Scalar(source, "id"), out var id) || id == Guid.Empty
                || Scalar(source, "category") != "Foci"
                || !int.TryParse(Scalar(source, "rating"), NumberStyles.None, CultureInfo.InvariantCulture, out int max)
                || max is < 1 or > 1000) return false;
            // Gear.Create's source grammar defines one as the minimum when minrating is absent.
            int min = 1;
            if (source.Element("minrating") is { } minimum
                && !int.TryParse(minimum.Value, NumberStyles.None, CultureInfo.InvariantCulture, out min)) return false;
            if (min < 1 || min > max) return false;
            string name = Scalar(source, "name"), book = Scalar(source, "source"), page = Scalar(source, "page");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(book) || string.IsNullOrWhiteSpace(page)) return false;
            bool tradition = false;
            string? requiredQuality = null;
            if (source.Element("required") is { } required)
            {
                if (required.HasAttributes || required.Elements().ToArray() is not [var one]
                    || one.Name != "oneof" || one.HasAttributes || one.Elements().ToArray() is not [var quality]
                    || quality.Name != "quality" || quality.HasAttributes || quality.HasElements
                    || string.IsNullOrWhiteSpace(quality.Value) || quality.Value.Trim() != quality.Value
                    || required.DescendantNodes().Any(n => n is not XElement && n.GetType() != typeof(XText))
                    || required.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                    || one.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) return false;
                requiredQuality = quality.Value;
            }
            if (source.Element("bonus") is { } bonus)
            {
                // No numerical source bonus is discarded. It needs its own effect compiler.
                if (bonus.HasAttributes || bonus.Elements().ToArray() is not [var prompt]
                    || prompt.Name != "selecttradition" || prompt.HasAttributes || prompt.HasElements
                    || !string.IsNullOrWhiteSpace(prompt.Value)
                    || bonus.Nodes().OfType<XText>().Any(n => !string.IsNullOrWhiteSpace(n.Value))) return false;
                tradition = true;
            }
            string cost = Scalar(source, "cost"), avail = Scalar(source, "avail");
            if (!TryEvaluate(cost, min, out _) || !TryAvailability(avail, min, out _, out _)) return false;
            definition = new(id, name, "Foci", min, max, cost, avail, book, page, tradition)
                { RequiredQuality1253 = requiredQuality };
            return true;
        }
        catch (Exception e) when (e is XmlException or ArgumentException or InvalidOperationException) { return false; }
    }

    public static bool TryEvaluate(string expression, int rating, out decimal value)
    {
        value = 0;
        if (rating <= 0 || expression is null) return false;
        if (decimal.TryParse(expression, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return value >= 0;
        if (expression.StartsWith('(') && expression.EndsWith(')')) expression = expression[1..^1];
        var match = Regex.Match(expression, @"^Rating \* (?<factor>[0-9]+(?:\.[0-9]+)?)$", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!match.Success || !decimal.TryParse(match.Groups["factor"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var factor)) return false;
        try { value = checked(rating * factor); return true; }
        catch (OverflowException) { return false; }
    }

    public static bool TryAvailability(string expression, int rating, out int value, out string legality)
    {
        value = 0; legality = CharacterCreationGearLegality.Legal;
        if (string.IsNullOrEmpty(expression)) return false;
        if (expression.EndsWith('R')) { legality = CharacterCreationGearLegality.Restricted; expression = expression[..^1]; }
        else if (expression.EndsWith('F')) { legality = CharacterCreationGearLegality.Forbidden; expression = expression[..^1]; }
        if (!TryEvaluate(expression, rating, out decimal result) || result != decimal.Truncate(result) || result > int.MaxValue) return false;
        value = (int)result; return true;
    }

    public static bool TryProject(CharacterCreationGearSelection selection, CharacterCreationGearCatalogOption option,
        IReadOnlyList<CharacterCreationGearCatalogOption> catalog, int maximumAvailability, int maximumQuantity,
        out CharacterCreationGearLine? line)
    {
        line = null;
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(catalog);
        if (selection.Quantity != 1 || selection.Focus is not { } focus
            || !TryProjectOne(focus, option, maximumAvailability, out var candidate)) return false;
        CharacterCreationOwnedGearProjection? preserved = null;
        if (focus.Preserve is { } inventory
            && !TryProjectInventory(inventory, catalog, maximumAvailability, maximumQuantity, out preserved)) return false;
        candidate = candidate! with { Focus = new(focus, preserved) };
        line = candidate with { LineDigest = CharacterCreationGearRules.ComputeLineDigest(candidate) };
        return true;
    }

    public static bool TryProjectInventory(CharacterCreationOwnedGearSelection inventory,
        IReadOnlyList<CharacterCreationGearCatalogOption> catalog, int maximumAvailability, int maximumQuantity,
        out CharacterCreationOwnedGearProjection? preserved)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);
        preserved = null;
        if (!CharacterCreationFinalizationDigest.IsCanonical(inventory.OriginalGraphDigest)
            || inventory.Items is not { Count: <= 4096 }
            || inventory.Items.Any(i => i is null || i.InstanceId == Guid.Empty
                || !CharacterCreationFinalizationDigest.IsCanonical(i.SavedGearDigest))
            || inventory.Items.Select(i => i.InstanceId).Distinct().Count() != inventory.Items.Count) return false;
        var rows = new List<CharacterCreationGearLine>();
        foreach (var item in inventory.Items)
        {
            var source = catalog.SingleOrDefault(o => o.OptionId == item.OptionId);
            if (source is null || !CharacterCreationGearRules.IsValidOption(source) || !source.IsSelectable
                || source.SourceNodeDigest != item.SourceNodeDigest || item.Quantity < 1 || item.Quantity > maximumQuantity) return false;
            CharacterCreationGearLine? row;
            if (item.Focus is { } ownedFocus)
            {
                if (ownedFocus.InstanceId != item.InstanceId || ownedFocus.Preserve is not null || item.Quantity != 1
                    || !TryProjectOne(ownedFocus, source, maximumAvailability, out row, retained: true)) return false;
            }
            else
            {
                if (source.Category == "Foci") return false;
                decimal total;
                try { total = checked(source.PackageCost * item.Quantity / source.PackageQuantity); }
                catch (OverflowException) { return false; }
                row = MakeLine(source, item.Quantity, source.PackageCost, total, source.Availability, source.Legality);
                row = row with { LineDigest = CharacterCreationGearRules.ComputeLineDigest(row) };
            }
            rows.Add(row!);
        }
        if (!ValidStacks1253(rows, retained: true, inventory.Stacks)) return false;
        try { preserved = new(inventory, rows.ToArray(), rows.Aggregate(0m, (sum, row) => checked(sum + row.TotalCost))); }
        catch (OverflowException) { return false; }
        return true;
    }

    private static bool TryProjectOne(CharacterCreationFocusSelection focus, CharacterCreationGearCatalogOption option,
        int maximumAvailability, out CharacterCreationGearLine? line, bool retained = false)
    {
        line = null;
        if (focus.InstanceId == Guid.Empty || focus.FocusId == Guid.Empty || focus.InstanceId == focus.FocusId
            || (focus.Stack is null ? focus.Bonded != focus.FocusId.HasValue : !focus.Bonded || focus.FocusId.HasValue)
            || string.IsNullOrWhiteSpace(focus.OwnerWorkspaceId.Value) || focus.SelectionId == Guid.Empty
            || !retained && !HasNewIdentity(focus, option.SourceId)
            || focus.Grade != string.Empty || focus.SourceNodeDigest != option.SourceNodeDigest
            || !TryReadSource(option.SourceNodeXml, out var source) || source != option.FocusSource || source!.SourceId != option.SourceId
            || source.Name != option.Name || source.Category != option.Category || source.SourceBook != option.SourceBook
            || source.Page != option.Page || focus.Rating < source.MinimumRating || focus.Rating > source.MaximumRating
            || focus.Extra is null || focus.Extra.Length > 256 || focus.Extra.Trim() != focus.Extra
            || (source.SelectTradition ? string.IsNullOrWhiteSpace(focus.Extra) || focus.ExtraSourceId is null
                    || focus.ExtraSourceId == Guid.Empty : focus.Extra.Length != 0 || focus.ExtraSourceId is not null)
            || !TryEvaluate(source.CostExpression, focus.Rating, out decimal cost)
            || !TryAvailability(source.AvailabilityExpression, focus.Rating, out int avail, out string legality)
            || avail > maximumAvailability) return false;
        var candidate = MakeLine(option, 1, cost, cost, avail, legality) with { Focus = new(focus, null) };
        line = candidate with { LineDigest = CharacterCreationGearRules.ComputeLineDigest(candidate) };
        return true;
    }

    // Stable across full review, cold replay and finalization; independent of the
    // mutable review digest, rating and price. Existing native instances instead
    // require an exact verified match to the original owner's saved graph.
    public static Guid InstanceIdFor(CharacterWorkspaceId owner, Guid selectionId, Guid sourceId)
        => Identity(owner, selectionId, sourceId, "gear");
    public static Guid BindingIdFor(CharacterWorkspaceId owner, Guid selectionId, Guid sourceId)
        => Identity(owner, selectionId, sourceId, "focus");
    private static Guid Identity(CharacterWorkspaceId owner, Guid selectionId, Guid sourceId, string kind)
    {
        if (string.IsNullOrWhiteSpace(owner.Value) || selectionId == Guid.Empty || sourceId == Guid.Empty)
            throw new ArgumentException("An owner, selection and canonical source identity are required.");
        string digest = CharacterCreationGearRules.Compute(new { Schema = "chummer.creation-focus-identity.v1", Owner = owner,
            Selection = selectionId, Source = sourceId, Kind = kind });
        return new Guid(Convert.FromHexString(digest[7..39]));
    }

    private static bool HasNewIdentity(CharacterCreationFocusSelection focus, Guid sourceId)
        => focus.InstanceId == InstanceIdFor(focus.OwnerWorkspaceId, focus.SelectionId, sourceId)
            && (focus.Stack is not null ? focus.FocusId is null
                : !focus.Bonded || focus.FocusId == BindingIdFor(focus.OwnerWorkspaceId, focus.SelectionId, sourceId));

    public static Guid StackIdentityFor1253(CharacterWorkspaceId owner, Guid token, bool gearOwner)
    {
        if (string.IsNullOrWhiteSpace(owner.Value) || token == Guid.Empty) throw new ArgumentException("Stack owner and token required.");
        string digest = CharacterCreationGearRules.Compute(new { Schema = "chummer.creation-stack-identity.v1",
            Owner = owner, Token = token, Kind = gearOwner ? "gear" : "stack" });
        return new Guid(Convert.FromHexString(digest[7..39]));
    }

    private static bool ValidStacks1253(IReadOnlyList<CharacterCreationGearLine> lines, bool retained,
        IReadOnlyList<CharacterCreationOwnedFocusStack1253>? originals = null)
    {
        var groups = lines.Where(l => l.Focus?.Selection.Stack is not null)
            .GroupBy(l => l.Focus!.Selection.Stack!.StackId).ToArray();
        if (retained && groups.Length != (originals?.Count ?? 0)) return false;
        var identities = new HashSet<Guid>(lines.Where(l => l.Focus is not null)
            .Select(l => l.Focus!.Selection.InstanceId));
        foreach (var group in groups)
        {
            var members = group.ToArray(); var focus = members[0].Focus!.Selection; var stack = focus.Stack!;
            if (stack.StackId == Guid.Empty || stack.OwnerInstanceId == Guid.Empty
                || !identities.Add(stack.StackId) || !identities.Add(stack.OwnerInstanceId)
                || stack.ComponentInstanceIds is not { Count: >= 2 and <= 6 }
                || stack.ComponentInstanceIds.Distinct().Count() != stack.ComponentInstanceIds.Count
                || members.Length != stack.ComponentInstanceIds.Count
                || members.Select(l => l.Focus!.Selection.InstanceId).Distinct().Count() != members.Length
                || !stack.ComponentInstanceIds.ToHashSet().SetEquals(members.Select(l => l.Focus!.Selection.InstanceId))
                || members.Any(l => CharacterCreationFinalizationDigest.Compute(l.Focus!.Selection.Stack)
                    != CharacterCreationFinalizationDigest.Compute(stack)
                    || l.Focus.Selection.OwnerWorkspaceId != focus.OwnerWorkspaceId)
                // Bounded baseline: higher-force stacks need their own profile consumer.
                || members.Sum(l => l.Focus!.Selection.Rating) > 6) return false;
            if (!retained && (stack.SelectionId == Guid.Empty
                || stack.StackId != StackIdentityFor1253(focus.OwnerWorkspaceId, stack.SelectionId, false)
                || stack.OwnerInstanceId != StackIdentityFor1253(focus.OwnerWorkspaceId, stack.SelectionId, true))) return false;
            if (retained && (originals!.Where(s => s.StackId == stack.StackId).ToArray() is not [var saved]
                || saved.OwnerInstanceId != stack.OwnerInstanceId || saved.Bonded != stack.Bonded
                || !saved.ComponentInstanceIds.SequenceEqual(stack.ComponentInstanceIds)
                || !CharacterCreationFinalizationDigest.IsCanonical(saved.SavedOwnerDigest)
                || !CharacterCreationFinalizationDigest.IsCanonical(saved.SavedStackDigest))) return false;
        }
        return true;
    }

    private static CharacterCreationGearLine MakeLine(CharacterCreationGearCatalogOption option, int quantity,
        decimal cost, decimal total, int avail, string legality)
        => new(option.OptionId, option.SourceId, option.Name, option.Category, quantity, option.PackageQuantity,
            cost, total, avail, legality, option.SourceBook, option.Page, option.SourceAnchorIds,
            option.SourceNodeXml, option.SourceNodeDigest, string.Empty);

    public static string[] RequiredOptionIds(IReadOnlyList<CharacterCreationGearSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        return selections.Select(s => s.OptionId).Concat(selections.SelectMany(s => s.Focus?.Preserve?.Items
                .Select(i => i.OptionId) ?? []))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static bool TryValidateBasket(IReadOnlyList<CharacterCreationGearLine> lines, out decimal retainedCost)
    {
        ArgumentNullException.ThrowIfNull(lines);
        retainedCost = 0;
        var focused = lines.Where(l => l.Focus is not null).ToArray();
        if (focused.Length == 0) return true;
        if (!ValidStacks1253(lines, retained: false)) return false;
        if (focused.Any(l => string.IsNullOrWhiteSpace(l.Focus!.Selection.OwnerWorkspaceId.Value)
            || l.Focus.Selection.SelectionId == Guid.Empty || l.SourceId == Guid.Empty
            || !HasNewIdentity(l.Focus.Selection, l.SourceId))) return false;
        var inventories = focused.Select(l => CharacterCreationFinalizationDigest.Compute(l.Focus!.Preserved)).Distinct().ToArray();
        if (inventories.Length != 1) return false;
        var preserved = focused[0].Focus!.Preserved;
        try
        {
            if (preserved is not null && (preserved.Lines is null
                || preserved.TotalCost != preserved.Lines.Aggregate(0m, (sum, row) => checked(sum + row.TotalCost)))) return false;
        }
        catch (OverflowException) { return false; }
        var all = focused.Concat(preserved?.Lines.Where(l => l.Focus is not null) ?? []).ToArray();
        var ids = new HashSet<Guid>();
        foreach (var row in all)
            if (!ids.Add(row.Focus!.Selection.InstanceId)
                || row.Focus.Selection.FocusId is { } focusId && !ids.Add(focusId)) return false;
        foreach (var item in preserved?.Selection.Items ?? [])
            if (item.Focus is null && !ids.Add(item.InstanceId)) return false;
        foreach (var stack in focused.Where(l => l.Focus!.Selection.Stack is not null)
            .Select(l => l.Focus!.Selection.Stack!).DistinctBy(s => s.StackId))
            if (!ids.Add(stack.StackId) || !ids.Add(stack.OwnerInstanceId)) return false;
        foreach (var stack in preserved?.Selection.Stacks ?? [])
            if (!ids.Add(stack.StackId) || !ids.Add(stack.OwnerInstanceId)) return false;
        retainedCost = preserved?.TotalCost ?? 0;
        return true;
    }

    private static string Scalar(XElement source, string name) => source.Element(name)?.Value ?? string.Empty;
}
