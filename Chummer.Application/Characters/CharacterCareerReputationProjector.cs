using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Reads the saved SR5 effect graph, never display totals or guessed UI values.
/// No XML or auxiliary state is written. Missing authoritative fields and
/// ambiguous relevant nodes fail closed instead of becoming zero.
/// </summary>
public static class CharacterCareerReputationProjector
{
    public const int MaximumCharacterXmlLength = 67_108_864;
    public const int MaximumRowsPerContainer = 100_000;
    private static readonly string[] Types =
        ["StreetCredMultiplier", "StreetCred", "Notoriety", "PublicAwareness", "Erased"];

    public static bool TryRead(
        WorkspaceStoredDocument saved, ICharacterSourceDataResolver sourceResolver,
        out CharacterCareerReputationSnapshot? snapshot, out string error)
        => TryReadCore(saved, sourceResolver, false, out snapshot, out error);

    // Inspect the exact saved state without pretending it is ready for another
    // mutation. In particular, never rewrite a dirty checkpoint or saturated
    // revision merely to make the normal command-admission reader succeed.
    internal static bool TryReadForContinuation(
        WorkspaceStoredDocument saved, ICharacterSourceDataResolver sourceResolver,
        out CharacterCareerReputationSnapshot? snapshot, out string error)
        => TryReadCore(saved, sourceResolver, true, out snapshot, out error);

    private static bool TryReadCore(
        WorkspaceStoredDocument saved, ICharacterSourceDataResolver sourceResolver,
        bool continuation, out CharacterCareerReputationSnapshot? snapshot, out string error)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        snapshot = null;
        error = "reputation_workspace_invalid";
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision <= 0
            || (continuation && (saved.SavedRevision < 0 || saved.SavedRevision > saved.ContentRevision))
            || (!continuation && saved.ContentRevision == long.MaxValue))
            return false;
        if (!continuation && saved.SavedRevision != saved.ContentRevision)
        {
            error = "reputation_workspace_not_clean";
            return false;
        }
        WorkspaceDocument document = saved.Document;
        if (document is null || document.Format != WorkspaceDocumentFormat.NativeXml
            || document.RulesetId != "sr5" || document.SchemaVersion != 1
            || document.PayloadKind is not ("workspace" or "sr5/chum5-xml"))
        {
            error = "reputation_workspace_not_sr5";
            return false;
        }
        try
        {
            XElement root = Parse(document.Content);
            if (!ReadBoolean(root, "created"))
            {
                error = "reputation_workspace_not_career";
                return false;
            }
            // These four scalar fields are emitted by Character.Save, unlike
            // calculated totals and CareerKarma, which are derived on demand.
            int streetCred = ReadInteger(root, "streetcred");
            int notoriety = ReadInteger(root, "notoriety");
            int awareness = ReadInteger(root, "publicawareness");
            int burnt = ReadInteger(root, "burntstreetcred");
            int careerKarma = ReadCareerKarma(root, out var expenses);
            Improvement[] improvements = ReadImprovements(root);
            HashSet<int> selected = [];
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string type in Types)
            {
                decimal total = SelectAndSum(improvements.Where(row => row.Type == type && row.Applicable), selected);
                // Erased is a selected-effect presence query, not a numeric
                // reputation bonus; its decimal value need not fit an integer.
                if (type != "Erased") values.Add(type, RoundAwayFromZero(total));
            }

            // Erased reads the legacy contributor list, not ValueOf's decimal.
            // In the pinned implementation a custom unique contributor is added
            // to the normal partition's list when that named partition exists;
            // for a custom-only named partition it is not returned by the cache.
            // Preserve this observable corner case rather than silently changing
            // imported characters by using Any(enabled Erased) instead.
            foreach (Improvement row in improvements.Where(row => row.Type == "Erased" && row.Custom && row.Unique.Length != 0))
            {
                if (!improvements.Any(other => other.Type == "Erased" && other.Applicable && !other.Custom && other.Name == row.Name))
                    selected.Remove(row.Index);
            }

            ICharacterSourceDataContext? context = sourceResolver.TryCreateContext(document.Content);
            error = "reputation_source_unavailable";
            if (context is null
                || !context.TryResolveCareerReputationSettings(out var settings, out string rawRuleState)
                || settings is null || string.IsNullOrEmpty(rawRuleState))
                return false;
            var inputs = new CharacterCareerReputationInputs("sr5", true, careerKarma,
                streetCred, notoriety, awareness, burnt,
                values["StreetCredMultiplier"], values["StreetCred"], values["Notoriety"],
                values["PublicAwareness"], settings.UseCalculatedPublicAwareness,
                improvements.Any(row => row.Type == "Erased" && selected.Contains(row.Index)));
            error = "reputation_calculation_invalid";
            if (!CharacterCareerReputationRules.TryProject(inputs, out var reputation)) return false;

            // Materialize all bindings before the final drift check; a source
            // context read is not a reservation or permission to save later.
            var candidate = new CharacterCareerReputationSnapshot(saved.Id,
                saved.ContentRevision, saved.SavedRevision,
                Digest("chummer.core.sr5-reputation-source/v1\0" + JsonSerializer.Serialize(new
                {
                    document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind,
                    PayloadDigest = Digest(document.Content)
                })), document.AuxiliaryStateDigest,
                Digest("chummer.core.sr5-reputation-rules/v1\0" + JsonSerializer.Serialize(new { settings, rawRuleState })), reputation!,
                Array.AsReadOnly(expenses), Array.AsReadOnly(improvements.Select(row =>
                    new CharacterCareerReputationImprovementContribution(row.Index, row.Type,
                        row.Name, row.Unique, row.Value, row.Custom, row.Applicable,
                        selected.Contains(row.Index))).ToArray()));
            error = "reputation_source_changed";
            if (!context.TryResolveCareerReputationSettings(out var finalSettings, out string finalRuleState)
                || settings != finalSettings || !string.Equals(rawRuleState, finalRuleState, StringComparison.Ordinal))
                return false;
            snapshot = candidate;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException
                                        or OverflowException or FormatException or IOException
                                        or UnauthorizedAccessException or ArgumentException)
        {
            error = "reputation_input_unresolved";
            return false;
        }
    }

    // Creation uses the same strict input parsers after materializing its
    // draft graph. Never let finalization turn malformed present data into
    // defaults or certify an unreadable persisted reputation/history shape.
    // This checks saved input shape, not settings/effect recalculation authority.
    internal static void ValidateSavedInputShape(XElement root)
    {
        if (root.Name != "character" || root.HasAttributes) throw Invalid();
        _ = ReadInteger(root, "streetcred");
        _ = ReadInteger(root, "notoriety");
        _ = ReadInteger(root, "publicawareness");
        if (ReadInteger(root, "burntstreetcred") < 0) throw Invalid();
        _ = ReadCareerKarma(root, out _);
        _ = ReadImprovements(root);
        _ = Rows(root, "contacts", "contact");
    }

    private static int ReadCareerKarma(
        XElement root, out CharacterCareerReputationExpenseContribution[] trace)
    {
        XElement[] rows = Rows(root, "expenses", "expense");
        trace = new CharacterCareerReputationExpenseContribution[rows.Length];
        int total = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            XElement row = rows[index];
            string type = Scalar(row, "type");
            // The legacy loader coerces unknown strings to Karma. That is not
            // acceptable as mutation authority: corrupt/future kinds are unresolved.
            if (type is not ("Karma" or "Nuyen")) throw Invalid();
            decimal amount = ReadDecimal(row, "amount");
            bool refund = ReadBoolean(row, "refund", false);
            bool forced = ReadBoolean(row, "forcecareervisible", false);
            bool included = type == "Karma" && !refund && (amount > 0 || forced);
            int contribution = included ? RoundAwayFromZero(amount) : 0;
            total = checked(total + contribution);
            trace[index] = new(index, amount, included, contribution);
        }
        return total;
    }

    private static Improvement[] ReadImprovements(XElement root)
    {
        XElement[] rows = Rows(root, "improvements", "improvement");
        var result = new List<Improvement>();
        for (int index = 0; index < rows.Length; index++)
        {
            XElement row = rows[index];
            string type = Scalar(row, "improvementttype");
            if (!Types.Contains(type, StringComparer.Ordinal))
            {
                if (Types.Contains(type, StringComparer.OrdinalIgnoreCase)) throw Invalid();
                continue;
            }
            // Numeric flags are how Improvement.Save serializes these fields.
            // Missing optional fields follow the inspected legacy constructor,
            // but malformed/duplicate present values never invoke defaults.
            bool enabled = ReadInteger(row, "enabled", 1) > 0;
            bool addToRating = ReadInteger(row, "addtorating", 0) > 0;
            string condition = Scalar(row, "condition", "", preserveWhitespace: true);
            result.Add(new(index, type, Scalar(row, "improvedname", "", preserveWhitespace: true),
                Scalar(row, "unique", "", preserveWhitespace: true),
                ReadDecimal(row, "val"), ReadBoolean(row, "custom", false),
                enabled && !addToRating && condition is "" or "career"));
        }
        return result.ToArray();
    }

    private static decimal SelectAndSum(IEnumerable<Improvement> rows, HashSet<int> selected)
    {
        decimal total = 0;
        foreach (var group in rows.GroupBy(row => row.Name, StringComparer.Ordinal))
        {
            decimal normal = SelectPartition(group.Where(row => !row.Custom).ToArray(), false, selected);
            decimal custom = SelectPartition(group.Where(row => row.Custom).ToArray(), true, selected);
            total = checked(total + checked(normal + custom));
        }
        return total;
    }

    private static decimal SelectPartition(Improvement[] rows, bool custom, HashSet<int> selected)
    {
        Improvement[] ordinary = rows.Where(row => row.Unique.Length == 0).ToArray();
        var picked = new List<Improvement>(ordinary);
        decimal total = Sum(ordinary);
        Improvement[] unique = rows.Where(row => row.Unique.Length != 0).ToArray();
        Improvement[]? precedence = null;
        if (!custom && unique.Any(row => row.Unique == "precedence0"))
            precedence = new[] { Highest(unique.Where(row => row.Unique == "precedence0")) }
                .Concat(unique.Where(row => row.Unique == "precedence-1")).ToArray();
        else if (!custom && unique.Any(row => row.Unique == "precedence1"))
            precedence = unique.Where(row => row.Unique is "precedence1" or "precedence-1").ToArray();

        if (precedence is not null)
        {
            decimal candidate = Sum(precedence);
            // A tie keeps ordinary improvements, including their zero-valued
            // presence. Other unique groups are suppressed by precedence.
            if (candidate > total) { total = candidate; picked = new(precedence); }
        }
        else
        {
            foreach (var group in unique.GroupBy(row => row.Unique, StringComparer.Ordinal))
            {
                Improvement highest = Highest(group);
                // The pinned loop uses decimal.MinValue as its no-winner
                // sentinel; an all-MinValue unique group contributes nothing.
                if (highest.Value == decimal.MinValue) continue;
                total = checked(total + highest.Value);
                picked.Add(highest);
            }
        }
        foreach (Improvement row in picked)
            if (row.Unique != "precedence0" || row.Value != decimal.MinValue || custom) selected.Add(row.Index);
        return total;
    }

    private static Improvement Highest(IEnumerable<Improvement> rows)
        => rows.Aggregate((best, row) => row.Value > best.Value ? row : best);

    private static decimal Sum(IEnumerable<Improvement> rows)
    {
        decimal total = 0;
        foreach (Improvement row in rows) total = checked(total + row.Value);
        return total;
    }

    private static int RoundAwayFromZero(decimal value)
        => decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));

    private static XElement Parse(string xml)
    {
        if (xml is not { Length: > 0 and <= MaximumCharacterXmlLength }) throw Invalid();
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacterXmlLength
        });
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw Invalid();
        if (root.Name != "character" || root.HasAttributes) throw Invalid();
        return root;
    }

    private static XElement[] Rows(XElement root, string containerName, string rowName)
    {
        XElement container = Child(root, containerName) ?? throw Invalid();
        if (container.HasAttributes || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw Invalid();
        XElement[] rows = container.Elements().Take(MaximumRowsPerContainer + 1).ToArray();
        if (rows.Length > MaximumRowsPerContainer || rows.Any(row => row.Name != rowName || row.HasAttributes))
            throw Invalid();
        return rows;
    }

    private static XElement? Child(XElement parent, string name)
    {
        XElement[] matches = parent.Elements().Where(element => element.Name.LocalName == name).Take(2).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && matches[0].Name != name) throw Invalid();
        return matches.SingleOrDefault();
    }

    private static string Scalar(XElement parent, string name, string? absent = null, bool preserveWhitespace = false)
    {
        XElement? child = Child(parent, name);
        if (child is null) return absent ?? throw Invalid();
        if (child.HasAttributes || child.HasElements) throw Invalid();
        string value = child.Value;
        if (!preserveWhitespace && !string.Equals(value, value.Trim(), StringComparison.Ordinal)) throw Invalid();
        return value;
    }

    private static int ReadInteger(XElement parent, string name, int? absent = null)
        => int.TryParse(Scalar(parent, name, absent?.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();

    private static decimal ReadDecimal(XElement parent, string name)
        => decimal.TryParse(Scalar(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal value) ? value : throw Invalid();

    private static bool ReadBoolean(XElement parent, string name, bool? absent = null)
        => bool.TryParse(Scalar(parent, name, absent?.ToString()), out bool value) ? value : throw Invalid();

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InvalidDataException Invalid() => new("reputation_input_unresolved");

    private sealed record Improvement(int Index, string Type, string Name, string Unique, decimal Value,
        bool Custom, bool Applicable);
}
