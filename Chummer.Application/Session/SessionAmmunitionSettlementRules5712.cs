using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>Writes proven absolute round custody; never calculates a fire cost or replays a purchase.</summary>
public static class SessionAmmunitionSettlementRules5712
{
    internal static SessionPlaySettlementContinuation5712 PreserveImportedHistory(SessionPlaySettlementContinuation5712 current,
        SessionPlaySettlementContinuation5712? prior)
    {
        if (prior is null) return current;
        var history = prior.ImportedHistory ?? [];
        if (Digest(prior.Origin) != Digest(current.Origin))
        {
            var segment = prior with { ImportedHistory = null, Digest = "" };
            segment = segment with { Digest = Digest(segment) };
            history = history.Append(segment).ToArray();
        }
        Require(history.Count <= 32, "ammunition-imported-history-capacity");
        current = current with { ImportedHistory = history.Count == 0 ? null : history, Digest = "" };
        return current with { Digest = Digest(current) };
    }

    public static IReadOnlyList<SessionAmmunitionOperation5712>? SettledOperations(SessionPlaySettlementContinuation5712? value)
        => value is null ? null : (value.ImportedHistory ?? []).Append(value)
            .SelectMany(x => x.Settlement.Review.Ammunition!.Operations)
            .GroupBy(x => x.OperationId, StringComparer.Ordinal).Select(x => x.First()).ToArray();

    public static SessionPlaySettlementIdentity5712? Identity(SessionPlaySettlementContinuation5712? value)
        => value is null ? null : new(Digest(value.Origin), Digest(value.Operations), Digest(value.Transfers),
            Digest(value.Recoveries), Digest(value.Settlement)) {
                FocusesDigest7490 = value.Focuses7490 is null ? null : Digest(value.Focuses7490) };

    public static bool RequiresSettlement(ISessionPlayReader? reader, SessionPlaySettlementIdentity5712? identity)
    {
        if (reader?.ReadOrigin() is not { } origin) return false;
        var operations = reader.ReadOperations(); var transfers = reader.ReadConditionTransfers();
        var recoveries = reader.ReadNaturalRecoveries();
        bool hasAmmunition = operations.Any(x => x.Intent.Events.Any(IsAmmunition)
            && (x.Result is null || x.Result is { Failure: null, Snapshot: not null }));
        if (identity is null) return hasAmmunition;
        return operations.Any(x => x.Result is null) || transfers.Any(x => x.Receipt is null) || recoveries.Any(x => x.Receipt is null)
            || identity.OriginDigest != Digest(origin) || identity.OperationsDigest != Digest(operations)
            || identity.RecoveriesDigest != Digest(recoveries) || transfers.Count == 0
            || identity.FocusesDigest7490 != (reader.ReadFocusContinuations7490().Count == 0 ? null : Digest(reader.ReadFocusContinuations7490()))
            || identity.TransfersDigest != Digest(transfers.Take(transfers.Count - 1).ToArray())
            || identity.SettlementDigest != Digest(transfers[^1].Intent);
    }

    public static bool SameQuantities(SessionPlayAmmunition? left, SessionPlayAmmunition? right)
        => left is null ? right is null : right is not null && Digest(Quantities(left)) == Digest(Quantities(right));

    private static object Quantities(SessionPlayAmmunition value) => new {
        Inventory = value.Inventory.OrderBy(x => x.GearId).Select(x => new { x.GearId, x.SourceId, x.TotalRounds, x.AvailableRounds }),
        Weapons = value.Weapons.OrderBy(x => x.WeaponId).Select(x => new { x.WeaponId, x.AmmoSlot, x.LoadedGearId, x.LoadedRounds })
    };

    internal static SessionAmmunitionSettlement5712? Review(ISessionPlayReader reader,
        SessionPlayAmmunition? canonical, SessionPlayAmmunition? live)
    {
        var operations = reader.ReadOperations();
        var ammo = operations.Where(x => x.Result is { Failure: null, Snapshot: not null }
            && x.Intent.Events.Any(IsAmmunition)).Select(x => new SessionAmmunitionOperation5712(
                x.Intent.OperationId, x.Intent.PayloadDigest, x.Intent.Events.Where(IsAmmunition).Select(e => e.EventId).ToArray())).ToArray();
        if (ammo.Length == 0) return null;
        Conflict(canonical is not null && live is not null, "ammunition-settlement-source-unavailable");
        var result = new SessionAmmunitionSettlement5712(canonical!, live!,
            Digest(new { Operations = operations, Transfers = reader.ReadConditionTransfers(), Recoveries = reader.ReadNaturalRecoveries() }), ammo) {
                EventCount = operations.Where(x => x.Result is { Failure: null, Snapshot: not null }).SelectMany(x => x.Intent.Events)
                    .Select(x => x.EventId).Distinct(StringComparer.Ordinal).Count(),
                PlayOrdinal = operations.LastOrDefault()?.Ordinal ?? 0
            };
        Validate(result); return result;
    }

    private static bool IsAmmunition(SessionEventEnvelope e) => e.EventType is SessionPlayEventTypes.WeaponFire
        or SessionPlayEventTypes.WeaponReload or SessionPlayEventTypes.WeaponUnload;

    internal static void ValidateIdentitySets(SessionPlayAmmunition before, SessionPlayAmmunition after)
    {
        Conflict(before.Inventory.Count == after.Inventory.Count && before.Weapons.Count == after.Weapons.Count
            && before.Inventory.Select(x => x.GearId).Distinct().Count() == before.Inventory.Count
            && before.Weapons.Select(x => x.WeaponId).Distinct().Count() == before.Weapons.Count
            && after.Inventory.Select(x => x.GearId).Distinct().Count() == after.Inventory.Count
            && after.Weapons.Select(x => x.WeaponId).Distinct().Count() == after.Weapons.Count
            && before.Inventory.Select(x => x.GearId).Order().SequenceEqual(after.Inventory.Select(x => x.GearId).Order())
            && before.Weapons.Select(x => x.WeaponId).Order().SequenceEqual(after.Weapons.Select(x => x.WeaponId).Order()),
            "ammunition-review-identities-changed");
    }

    public static void Validate(SessionAmmunitionSettlement5712 review)
    {
        Require(review is not null && review.Before is not null && review.After is not null
            && review.HistoryDigest.Length == 64 && review.Operations.Count is > 0 and <= SessionPlayLimits.MaxOperations,
            "ammunition-settlement-review-invalid");
        var before = review.Before; var after = review.After;
        ValidateIdentitySets(before, after);
        Require(before.Revision.Length == 64 && after.Revision.Length == 64
            && before.Inventory.Count == after.Inventory.Count && before.Weapons.Count == after.Weapons.Count
            && after.Inventory.Count <= 256 && after.Weapons.Count <= 64, "ammunition-settlement-shape-changed");
        var stock = before.Inventory.ToDictionary(x => x.GearId);
        var weapons = before.Weapons.ToDictionary(x => x.WeaponId);
        Require(after.Inventory.Select(x => x.GearId).Distinct().Count() == stock.Count
            && after.Weapons.Select(x => x.WeaponId).Distinct().Count() == weapons.Count,
            "ammunition-settlement-duplicate-identity");
        foreach (var item in after.Inventory)
        {
            Conflict(stock.TryGetValue(item.GearId, out var old) && item.SourceId == old.SourceId
                && item.TotalRounds >= 0 && item.TotalRounds <= old.TotalRounds && item.AvailableRounds >= 0
                && item.AvailableRounds + after.Weapons.Where(x => x.LoadedGearId == item.GearId).Sum(x => (long)x.LoadedRounds) == item.TotalRounds,
                "ammunition-settlement-stock-divergence");
        }
        foreach (var item in after.Weapons)
        {
            Conflict(weapons.TryGetValue(item.WeaponId, out var old) && item.AmmoSlot == old.AmmoSlot
                && Digest(item.Feeds) == Digest(old.Feeds) && Digest(item.CompatibleGearIds) == Digest(old.CompatibleGearIds)
                && item.LoadedRounds >= 0 && item.Feeds.Any(x => x.Capacity >= item.LoadedRounds)
                && (item.Feed is null || item.Feeds.Any(x => x.Id == item.Feed && x.Capacity >= item.LoadedRounds))
                && (item.LoadedGearId == Guid.Empty ? item.LoadedRounds == 0 : stock.ContainsKey(item.LoadedGearId)
                    && item.CompatibleGearIds.Contains(item.LoadedGearId)), "ammunition-settlement-clip-divergence");
        }
    }

    // Rehydrate the selected feed from retained custody only while canonical stock and clips still match.
    // XML's historical clip schema has no feed field; source-admitted feeds remain the only legal choices.
    public static SessionPlayAmmunition? RetainFeed(SessionPlayAmmunition? projected,
        SessionPlaySettlementContinuation5712? continuation)
    {
        if (projected is null || continuation?.Settlement.Review.Ammunition is not { } review
            || !SameQuantities(projected, review.After)) return projected;
        var prior = review.After.Weapons.ToDictionary(x => x.WeaponId);
        var weapons = projected.Weapons.Select(x => prior.TryGetValue(x.WeaponId, out var old)
            && old.Feed is not null && x.Feeds.Any(f => f.Id == old.Feed && f.Capacity >= x.LoadedRounds)
            ? x with { Feed = old.Feed } : x).ToArray();
        return projected with { Weapons = weapons, Revision = Digest(new { projected.Revision, feeds = weapons.Select(x => new { x.WeaponId, x.Feed }) }) };
    }

    public static string Apply(string original, SessionAmmunitionSettlement5712 review,
        SessionPlayConditionState5412? condition = null)
    {
        Validate(review);
        using var reader = XmlReader.Create(new StringReader(original), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = SessionPlayLimits.MaxCharacterBytes });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root!;
        Conflict(root.Name == "character", "ammunition-character-required");
        var replacements = new List<(int Start, int Length, string Text)>();
        if (condition is not null)
        {
            Damage("physicalcmfilled", checked(condition.Physical + condition.Overflow));
            Damage("stuncmfilled", condition.Stun);
        }
        foreach (var item in review.After.Inventory)
        {
            var before = review.Before.Inventory.Single(x => x.GearId == item.GearId);
            var row = Unique(root, "gear", item.GearId);
            var quantity = row.Elements("qty").Single();
            Conflict(decimal.TryParse(quantity.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                && value == before.TotalRounds, "ammunition-xml-stock-stale");
            if (before.TotalRounds != item.TotalRounds) ReplaceValue(quantity, item.TotalRounds.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var item in review.After.Weapons)
        {
            var before = review.Before.Weapons.Single(x => x.WeaponId == item.WeaponId);
            var row = Unique(root, "weapon", item.WeaponId);
            var clips = row.Elements("clips").SingleOrDefault();
            var clip = clips?.Elements("clip").Single();
            Conflict((clip is null ? Guid.Empty : Guid.Parse(clip.Element("id")!.Value)) == before.LoadedGearId
                && (clip is null ? 0 : int.Parse(clip.Element("count")!.Value, CultureInfo.InvariantCulture)) == before.LoadedRounds,
                "ammunition-xml-clip-stale");
            if (clip is not null)
            {
                if (before.LoadedGearId != item.LoadedGearId) ReplaceValue(clip.Element("id")!, item.LoadedGearId.ToString("D"));
                if (before.LoadedRounds != item.LoadedRounds) ReplaceValue(clip.Element("count")!, item.LoadedRounds.ToString(CultureInfo.InvariantCulture));
            }
            else if (item.LoadedGearId != Guid.Empty)
            {
                int start = Start(row); int end = End(row, start);
                int close = original.LastIndexOf("</weapon", end - 1, end - start, StringComparison.Ordinal);
                Conflict(close >= start, "ammunition-xml-weapon-boundary");
                replacements.Add((close, 0, "<clips><clip><id>" + item.LoadedGearId.ToString("D")
                    + "</id><count>" + item.LoadedRounds.ToString(CultureInfo.InvariantCulture) + "</count></clip></clips>"));
            }
        }
        foreach (var change in replacements.OrderByDescending(x => x.Start))
            original = original.Remove(change.Start, change.Length).Insert(change.Start, change.Text);
        return original;

        // All edits use offsets from the original. Opaque XML, comments, declaration and line endings stay byte-for-byte.
        int Start(XElement element)
        {
            var line = (IXmlLineInfo)element;
            int at = LineStart(0, line.LineNumber) + line.LinePosition - 1;
            if (at > 0 && original[at - 1] == '<') at--;
            Conflict(original[at] == '<', "ammunition-xml-position"); return at;
        }
        int End(XElement element, int start)
        {
            // Locate the matching closing tag using XML line information on the next sibling/ancestor boundary.
            int depth = 0; int at = start;
            using var scan = XmlReader.Create(new StringReader(original[start..]), new XmlReaderSettings {
                ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (scan.Read())
            {
                if (scan.NodeType == XmlNodeType.Element && !scan.IsEmptyElement) depth++;
                if (scan.NodeType == XmlNodeType.EndElement && --depth == 0)
                {
                    var info = (IXmlLineInfo)scan;
                    at = LineStart(start, info.LineNumber) + info.LinePosition - 1;
                    return original.IndexOf('>', at) + 1;
                }
            }
            throw new SessionPlayException(SessionPlayFailures.Conflict("ammunition-xml-boundary"));
        }
        void ReplaceValue(XElement element, string value)
        {
            int start = Start(element); int open = OpenEnd(start);
            Conflict(open >= start && original[open - 1] != '/', "ammunition-xml-scalar-empty");
            int close = original.IndexOf('<', open + 1);
            Conflict(close >= open && original.AsSpan(close).StartsWith("</" + element.Name.LocalName, StringComparison.Ordinal),
                "ammunition-xml-scalar-markup");
            replacements.Add((open + 1, close - open - 1, value));
        }
        int LineStart(int start, int line)
        {
            for (int n = 1; n < line; n++)
            {
                int next = original.IndexOfAny(['\r', '\n'], start);
                Conflict(next >= 0, "ammunition-xml-position");
                start = next + 1;
                if (original[next] == '\r' && start < original.Length && original[start] == '\n') start++;
            }
            return start;
        }
        int OpenEnd(int start)
        {
            char quote = '\0';
            for (int n = start; n < original.Length; n++)
            {
                char c = original[n];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '>') return n;
            }
            throw new SessionPlayException(SessionPlayFailures.Conflict("ammunition-xml-open-boundary"));
        }
        void Damage(string name, int value)
        {
            var scalar = root.Elements(name).SingleOrDefault();
            if (scalar is not null)
            {
                if (int.Parse(scalar.Value, CultureInfo.InvariantCulture) != value)
                    ReplaceValue(scalar, value.ToString(CultureInfo.InvariantCulture));
            }
            else if (value != 0)
            {
                int close = original.LastIndexOf("</character", StringComparison.Ordinal);
                Conflict(close >= 0, "ammunition-xml-root-boundary");
                replacements.Add((close, 0, "<" + name + ">" + value.ToString(CultureInfo.InvariantCulture) + "</" + name + ">"));
            }
        }
    }

    private static XElement Unique(XElement root, string name, Guid id)
    {
        var rows = root.Descendants(name).Where(x => Guid.TryParse(x.Element("guid")?.Value, out var value) && value == id).Take(2).ToArray();
        Conflict(rows.Length == 1, "ammunition-xml-identity-not-unique"); return rows[0];
    }
}
