using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Persistence-boundary proof for the append-only creation-lifestyle receipt lane.
/// It validates the exact create/edit/delete XML delta without consulting UI state.
/// </summary>
public static class CharacterCreationLifestyleReceiptLedgerIntegrity
{
    public const int MaximumEntries = 4096;

    private static readonly HashSet<string> s_KnownLifestyleElements = new(StringComparer.Ordinal)
    {
        "sourceid", "guid", "name", "cost", "dice", "lp", "baselifestyle", "multiplier",
        "months", "roommates", "percentage", "area", "comforts", "security", "basearea",
        "basecomforts", "basesecurity", "maxarea", "maxcomforts", "maxsecurity", "costforearea",
        "costforarea", "costforcomforts", "costforsecurity", "allowbonuslp", "bonuslp", "source",
        "page", "trustfund", "splitcostwithroommates", "type", "increment", "city", "district",
        "borough", "lifestylequalities"
    };

    private static readonly HashSet<string> s_KnownQualityElements = new(StringComparer.Ordinal)
    {
        "sourceid", "guid", "name", "category", "extra", "cost", "multiplier", "basemultiplier",
        "lp", "areamaximum", "comfortsmaximum", "securitymaximum", "area", "comforts", "security",
        "uselpcost", "print", "lifestylequalitytype", "lifestylequalitysource", "free", "isfreegrid",
        "source", "page", "allowed"
    };

    public static string ComputeContentDigest(string content) => "sha256:"
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? entries)
    {
        if (string.IsNullOrWhiteSpace(workspaceId.Value)
            || currentContentRevision <= 0
            || entries is null
            || entries.Count > MaximumEntries)
            return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long previousRevision = 0;
        foreach (CharacterCreationLifestyleReceiptLedgerEntry? entry in entries)
        {
            if (!IsValidPersistedEntry(workspaceId, currentContentRevision, entry)
                || !keys.Add(entry!.IdempotencyKeyDigest)
                || !ids.Add(entry.Receipt.ReceiptId)
                || entry.Receipt.ContentRevision < previousRevision)
                return false;
            previousRevision = entry.Receipt.ContentRevision;
        }
        return true;
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? current,
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? replacement)
    {
        current ??= [];
        replacement ??= [];
        if (nextContentRevision != previousContentRevision + 1
            || replacement.Count != current.Count + 1
            || replacement.Count > MaximumEntries
            || !IsValidLedger(workspaceId, nextContentRevision, replacement))
            return false;
        for (int index = 0; index < current.Count; index++)
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    current[index],
                    replacement[index]))
                return false;
        }
        return IsValidForCommit(
            workspaceId,
            previousContentRevision,
            previousSavedRevision,
            replacement[^1]);
    }

    public static bool HasValidContentTransition(
        CharacterCreationLifestyleReceiptLedgerEntry entry,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(replacementDocument);
        CharacterCreationLifestyleReceipt receipt = entry.Receipt;
        CharacterCreationLifestyleAtomicWritePlan plan = receipt.WritePlan;
        if (!FixedEquals(receipt.ContentDigestBefore, ComputeContentDigest(currentDocument.Content))
            || !FixedEquals(receipt.ContentDigestAfter, ComputeContentDigest(replacementDocument.Content))
            || currentDocument.Format != replacementDocument.Format
            || !string.Equals(currentDocument.RulesetId, replacementDocument.RulesetId, StringComparison.Ordinal)
            || currentDocument.SchemaVersion != replacementDocument.SchemaVersion
            || !string.Equals(currentDocument.PayloadKind, replacementDocument.PayloadKind, StringComparison.Ordinal))
            return false;

        try
        {
            XDocument current = XDocument.Parse(currentDocument.Content, LoadOptions.PreserveWhitespace);
            XDocument replacement = XDocument.Parse(replacementDocument.Content, LoadOptions.PreserveWhitespace);
            if (current.Root is not XElement currentRoot
                || replacement.Root is not XElement replacementRoot
                || !string.Equals(currentRoot.Name.LocalName, "character", StringComparison.Ordinal)
                || !string.Equals(replacementRoot.Name.LocalName, "character", StringComparison.Ordinal)
                || ParseBool(ReadValue(currentRoot, "created"))
                || ParseBool(ReadValue(replacementRoot, "created")))
                return false;

            XElement? beforeElement = FindUniqueLifestyle(currentRoot, receipt.LifestyleId);
            XElement? afterElement = FindUniqueLifestyle(replacementRoot, receipt.LifestyleId);
            if (!HasExpectedPresence(receipt.MutationKind, beforeElement, afterElement)
                || plan.Before is not null && !ElementMatchesProjection(beforeElement!, plan.Before)
                || plan.After is not null && !ElementMatchesProjection(afterElement!, plan.After))
                return false;

            XDocument expected = new(current);
            XElement expectedRoot = expected.Root!;
            XElement? expectedTarget = FindUniqueLifestyle(expectedRoot, receipt.LifestyleId);
            switch (receipt.MutationKind)
            {
                case CharacterCreationLifestyleMutationKinds.Create:
                    XElement expectedContainer = expectedRoot.Element("lifestyles")
                        ?? AddAndReturn(expectedRoot, new XElement("lifestyles"));
                    expectedContainer.Add(new XElement(afterElement!));
                    break;
                case CharacterCreationLifestyleMutationKinds.Edit:
                    expectedTarget!.ReplaceWith(new XElement(afterElement!));
                    break;
                case CharacterCreationLifestyleMutationKinds.Delete:
                    expectedTarget!.Remove();
                    break;
                default:
                    return false;
            }
            if (!XNode.DeepEquals(expected, replacement))
                return false;

            IReadOnlySet<Guid> retainedIds = plan.After?.Configuration.Qualities
                .Select(item => item.InstanceId)
                .ToHashSet() ?? new HashSet<Guid>();
            string siblingBefore = ComputeUntouchedSiblingDigest(currentRoot, receipt.LifestyleId);
            string siblingAfter = ComputeUntouchedSiblingDigest(replacementRoot, receipt.LifestyleId);
            string nestedBefore = beforeElement is null
                || receipt.MutationKind == CharacterCreationLifestyleMutationKinds.Delete
                ? EmptyDigest()
                : ComputeNestedStateDigest(beforeElement, retainedIds);
            string nestedAfter = afterElement is null
                || receipt.MutationKind == CharacterCreationLifestyleMutationKinds.Create
                ? EmptyDigest()
                : ComputeNestedStateDigest(afterElement, retainedIds);
            return FixedEquals(plan.UntouchedSiblingDigestBefore, siblingBefore)
                && FixedEquals(plan.UntouchedSiblingDigestAfter, siblingAfter)
                && FixedEquals(plan.NestedStateDigestBefore, nestedBefore)
                && FixedEquals(plan.NestedStateDigestAfter, nestedAfter);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static bool IsValidForCommit(
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        long expectedSavedRevision,
        CharacterCreationLifestyleReceiptLedgerEntry? entry) =>
        IsStructurallyValid(entry)
        && entry!.Receipt.WorkspaceId == workspaceId
        && entry.Receipt.PreviousWorkspaceRevision == expectedContentRevision
        && entry.Receipt.WorkspaceRevision == expectedContentRevision + 1
        && entry.Receipt.PreviousContentRevision == expectedContentRevision
        && entry.Receipt.ContentRevision == expectedContentRevision + 1
        && entry.Receipt.PreviousSavedRevision == expectedSavedRevision
        && entry.Receipt.SavedRevision == expectedContentRevision + 1;

    public static bool IsValidPersistedEntry(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationLifestyleReceiptLedgerEntry? entry) =>
        IsStructurallyValid(entry)
        && entry!.Receipt.WorkspaceId == workspaceId
        && entry.Receipt.ContentRevision <= currentContentRevision;

    private static bool IsStructurallyValid(CharacterCreationLifestyleReceiptLedgerEntry? entry)
    {
        if (entry?.Receipt is not CharacterCreationLifestyleReceipt receipt
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(entry.IdempotencyKeyDigest)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(entry.CommandDigest)
            || !FixedEquals(entry.IdempotencyKeyDigest, receipt.IdempotencyKeyDigest)
            || !FixedEquals(entry.CommandDigest, receipt.CommandDigest)
            || !string.Equals(receipt.Schema, CharacterCreationLifestylesSchemas.ReceiptV1, StringComparison.Ordinal)
            || !string.Equals(receipt.StepId, CharacterCreationWizardStepIds.ContactsLifestyles, StringComparison.Ordinal)
            || !CharacterCreationLifestyleMutationKinds.All.Contains(receipt.MutationKind)
            || receipt.LifestyleId == Guid.Empty
            || string.IsNullOrWhiteSpace(receipt.ReceiptId)
            || receipt.ReceiptId.Length != 43
            || !receipt.ReceiptId.StartsWith("creation-lifestyle-", StringComparison.Ordinal)
            || receipt.PreviousWorkspaceRevision <= 0
            || receipt.WorkspaceRevision != receipt.PreviousWorkspaceRevision + 1
            || receipt.PreviousContentRevision != receipt.PreviousWorkspaceRevision
            || receipt.ContentRevision != receipt.WorkspaceRevision
            || receipt.PreviousSavedRevision < 0
            || receipt.PreviousSavedRevision > receipt.PreviousContentRevision
            || receipt.SavedRevision != receipt.ContentRevision
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.ContentDigestBefore)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.ContentDigestAfter)
            || FixedEquals(receipt.ContentDigestBefore, receipt.ContentDigestAfter)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.SourceDigest)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.RulesDigest)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.RuntimeDigest)
            || receipt.LifestyleCostBefore < 0m
            || receipt.LifestyleCostAfter < 0m
            || receipt.LifestyleBudgetRemaining < 0m
            || !IsValidWritePlan(receipt.WritePlan, receipt)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(receipt.ReceiptDigest)
            || !FixedEquals(receipt.ReceiptDigest, CharacterCreationLifestylesRules.ComputeReceiptDigest(receipt)))
            return false;
        string expectedId = "creation-lifestyle-" + entry.CommandDigest["sha256:".Length..][..24];
        return string.Equals(receipt.ReceiptId, expectedId, StringComparison.Ordinal);
    }

    private static bool IsValidWritePlan(
        CharacterCreationLifestyleAtomicWritePlan? plan,
        CharacterCreationLifestyleReceipt receipt)
    {
        if (plan is null
            || !string.Equals(plan.Schema, CharacterCreationLifestylesSchemas.WritePlanV1, StringComparison.Ordinal)
            || !string.Equals(plan.StepId, CharacterCreationWizardStepIds.ContactsLifestyles, StringComparison.Ordinal)
            || !string.Equals(plan.MutationKind, receipt.MutationKind, StringComparison.Ordinal)
            || plan.LifestyleId != receipt.LifestyleId
            || plan.Operations is not { Count: 1 }
            || plan.Operations[0] is not CharacterCreationLifestyleWriteOperation operation
            || operation.Order != 1
            || !string.Equals(operation.MutationKind, receipt.MutationKind, StringComparison.Ordinal)
            || operation.LifestyleId != receipt.LifestyleId
            || operation.SourceAnchorIds is not { Count: > 0 }
            || !operation.SourceAnchorIds.SequenceEqual(CharacterCreationLifestyleSourceAnchors.All)
            || !FixedEquals(plan.ContentDigestBefore, receipt.ContentDigestBefore)
            || !FixedEquals(plan.ContentDigestAfter, receipt.ContentDigestAfter)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(plan.UntouchedSiblingDigestBefore)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(plan.UntouchedSiblingDigestAfter)
            || !FixedEquals(plan.UntouchedSiblingDigestBefore, plan.UntouchedSiblingDigestAfter)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(plan.NestedStateDigestBefore)
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(plan.NestedStateDigestAfter)
            || !FixedEquals(plan.NestedStateDigestBefore, plan.NestedStateDigestAfter)
            || !plan.PreservesUntouchedSiblingState
            || !plan.PreservesNestedState
            || !HasPlanPresence(plan)
            || plan.Before is not null && !IsValidProjection(plan.Before)
            || plan.After is not null && !IsValidProjection(plan.After)
            || !FixedEquals(operation.BeforeDigest, plan.Before?.LifestyleDigest ?? EmptyDigest())
            || !FixedEquals(operation.AfterDigest, plan.After?.LifestyleDigest ?? EmptyDigest())
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(plan.PlanDigest)
            || !FixedEquals(plan.PlanDigest, CharacterCreationLifestylesRules.ComputePlanDigest(plan)))
            return false;
        return true;
    }

    private static bool HasPlanPresence(CharacterCreationLifestyleAtomicWritePlan plan) =>
        plan.MutationKind switch
        {
            CharacterCreationLifestyleMutationKinds.Create => plan.Before is null
                && plan.After?.Configuration.LifestyleId == plan.LifestyleId,
            CharacterCreationLifestyleMutationKinds.Edit => plan.Before?.Configuration.LifestyleId == plan.LifestyleId
                && plan.After?.Configuration.LifestyleId == plan.LifestyleId,
            CharacterCreationLifestyleMutationKinds.Delete => plan.Before?.Configuration.LifestyleId == plan.LifestyleId
                && plan.After is null,
            _ => false
        };

    private static bool IsValidProjection(CharacterCreationLifestyleProjection projection) =>
        projection.SourceId != Guid.Empty
        && projection.Configuration.LifestyleId != Guid.Empty
        && CharacterCreationLifestylesRules.IsCanonicalDigest(projection.LifestyleDigest)
        && FixedEquals(
            projection.LifestyleDigest,
            CharacterCreationLifestylesRules.ComputeProjectionDigest(projection));

    private static bool HasExpectedPresence(
        string mutation,
        XElement? before,
        XElement? after) => mutation switch
        {
            CharacterCreationLifestyleMutationKinds.Create => before is null && after is not null,
            CharacterCreationLifestyleMutationKinds.Edit => before is not null && after is not null,
            CharacterCreationLifestyleMutationKinds.Delete => before is not null && after is null,
            _ => false
        };

    private static bool ElementMatchesProjection(
        XElement element,
        CharacterCreationLifestyleProjection projection)
    {
        CharacterCreationLifestyleConfiguration config = projection.Configuration;
        if (!Guid.TryParseExact(ReadValue(element, "guid"), "D", out Guid id)
            || id != config.LifestyleId
            || !TryReadUniqueSourceId(element, out Guid sourceId)
            || sourceId != projection.SourceId
            || !string.Equals(ReadValue(element, "name"), config.Name, StringComparison.Ordinal)
            || !string.Equals(ReadValue(element, "baselifestyle"), projection.BaseLifestyleName, StringComparison.Ordinal)
            || !MatchesInt(element, "months", config.Increments)
            || !MatchesDecimal(element, "percentage", config.Percentage)
            || !MatchesInt(element, "roommates", config.Roommates)
            || ParseBool(ReadValue(element, "splitcostwithroommates")) != config.SplitCostWithRoommates
            || ParseBool(ReadValue(element, "trustfund")) != config.TrustFund
            || !MatchesInt(element, "area", config.Area)
            || !MatchesInt(element, "comforts", config.Comforts)
            || !MatchesInt(element, "security", config.Security)
            || !MatchesInt(element, "bonuslp", config.BonusLifestylePoints)
            || !string.Equals(ReadValue(element, "city"), config.City, StringComparison.Ordinal)
            || !string.Equals(ReadValue(element, "district"), config.District, StringComparison.Ordinal)
            || !string.Equals(ReadValue(element, "borough"), config.Borough, StringComparison.Ordinal)
            || !string.Equals(ReadValue(element, "type"), LegacyStyle(config.StyleId), StringComparison.Ordinal)
            || !string.Equals(ReadValue(element, "increment"), LegacyIncrement(config.IncrementId), StringComparison.Ordinal))
            return false;

        XElement[] rows = element.Element("lifestylequalities")?.Elements("lifestylequality").ToArray() ?? [];
        if (rows.Length != config.Qualities.Count)
            return false;
        var seen = new HashSet<Guid>();
        foreach (CharacterCreationLifestyleQualitySelection selection in config.Qualities)
        {
            XElement[] matches = rows.Where(row => Guid.TryParseExact(
                    ReadValue(row, "guid"),
                    "D",
                    out Guid qualityId) && qualityId == selection.InstanceId)
                .Take(2)
                .ToArray();
            if (matches.Length != 1
                || !seen.Add(selection.InstanceId)
                || !TryReadUniqueSourceId(matches[0], out Guid qualitySourceId)
                || !TryParseOptionSourceId(selection.OptionId, out Guid expectedSourceId)
                || qualitySourceId != expectedSourceId
                || !string.Equals(ReadValue(matches[0], "extra"), selection.Extra, StringComparison.Ordinal)
                || ParseBool(ReadValue(matches[0], "uselpcost")) != selection.UseLifestylePoints
                || ParseBool(ReadValue(matches[0], "free")) != selection.IsFree
                || ParseBool(ReadValue(matches[0], "isfreegrid")) != selection.IsBuiltIn)
                return false;
        }
        return true;
    }

    internal static bool ElementMatchesProjectionForTests(
        string elementXml,
        CharacterCreationLifestyleProjection projection) =>
        ElementMatchesProjection(XElement.Parse(elementXml, LoadOptions.PreserveWhitespace), projection);

    internal static string ComputeUntouchedSiblingDigestForTests(string characterXml, Guid targetId)
    {
        XDocument document = XDocument.Parse(characterXml, LoadOptions.PreserveWhitespace);
        return ComputeUntouchedSiblingDigest(document.Root!, targetId);
    }

    private static bool TryParseOptionSourceId(string optionId, out Guid id)
    {
        const string prefix = "lifestyle-quality:";
        id = Guid.Empty;
        return optionId.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(optionId[prefix.Length..], "D", out id)
            && id != Guid.Empty;
    }

    private static string ComputeUntouchedSiblingDigest(XElement root, Guid targetId)
    {
        XElement? target = FindUniqueLifestyle(root, targetId);
        return CanonicalDigest((root.Element("lifestyles")?.Nodes() ?? [])
            .Where(node => !ReferenceEquals(node, target))
            .Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
            .Select(node => node.ToString(SaveOptions.DisableFormatting))
            .ToArray());
    }

    private static string ComputeNestedStateDigest(XElement lifestyle, IReadOnlySet<Guid> retainedQualityIds)
    {
        string[] qualityState = (lifestyle.Element("lifestylequalities")?.Elements("lifestylequality") ?? [])
            .Where(row => Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid id)
                          && retainedQualityIds.Contains(id))
            .Select(row => new
            {
                Id = ReadValue(row, "guid"),
                Attributes = row.Attributes().Select(attribute => attribute.ToString()).ToArray(),
                Unknown = row.Nodes()
                    .Where(node => node is not XElement element
                                   || !s_KnownQualityElements.Contains(element.Name.LocalName))
                    .Select(node => node.ToString(SaveOptions.DisableFormatting))
                    .ToArray()
            })
            .Where(item => item.Attributes.Length != 0 || item.Unknown.Length != 0)
            .Select(item => CanonicalDigest(item))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return CanonicalDigest(new
        {
            Attributes = lifestyle.Attributes().Select(attribute => attribute.ToString()).ToArray(),
            Unknown = lifestyle.Nodes()
                .Where(node => node is not XElement element
                               || !s_KnownLifestyleElements.Contains(element.Name.LocalName))
                .Select(node => node.ToString(SaveOptions.DisableFormatting))
                .ToArray(),
            QualityState = qualityState
        });
    }

    private static XElement? FindUniqueLifestyle(XElement root, Guid id)
    {
        XElement[] matches = (root.Element("lifestyles")?.Elements("lifestyle") ?? [])
            .Where(row => Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid parsed) && parsed == id)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool TryReadUniqueSourceId(XElement element, out Guid id)
    {
        Guid[] ids = element.Elements("sourceid")
            .Select(row => Guid.TryParse(row.Value.Trim(), out Guid parsed) ? parsed : Guid.Empty)
            .Distinct()
            .ToArray();
        id = ids.Length == 1 ? ids[0] : Guid.Empty;
        return id != Guid.Empty;
    }

    private static bool MatchesInt(XElement element, string name, int expected) =>
        int.TryParse(ReadValue(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
        && value == expected;

    private static bool MatchesDecimal(XElement element, string name, decimal expected) =>
        decimal.TryParse(ReadValue(element, name), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
        && value == expected;

    private static string LegacyStyle(string style) => style switch
    {
        CharacterCreationLifestyleStyleIds.Advanced => "Advanced",
        CharacterCreationLifestyleStyleIds.BoltHole => "BoltHole",
        CharacterCreationLifestyleStyleIds.Safehouse => "Safehouse",
        _ => "Standard"
    };

    private static string LegacyIncrement(string increment) => increment switch
    {
        CharacterCreationLifestyleIncrementIds.Day => "Day",
        CharacterCreationLifestyleIncrementIds.Week => "Week",
        _ => "Month"
    };

    private static XElement AddAndReturn(XElement parent, XElement child)
    {
        parent.Add(child);
        return child;
    }

    private static string CanonicalDigest<T>(T value) =>
        CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);

    private static string EmptyDigest() => CanonicalDigest(Array.Empty<string>());

    private static string ReadValue(XElement parent, string name)
        => parent.Element(name)?.Value.Trim() ?? string.Empty;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
            || string.Equals(value, "1", StringComparison.Ordinal);

    private static bool FixedEquals(string? left, string? right)
        => CharacterCreationLifestylesRules.DigestsEqual(left, right);
}
