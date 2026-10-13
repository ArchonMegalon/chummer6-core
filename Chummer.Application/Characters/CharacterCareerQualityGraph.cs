using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Prepares the actual saved quality/improvement delta for the Career executor.
/// Does not grant owner, source, eligibility, GM or store authority. No I/O is
/// performed; the executor must re-resolve sources and commit the complete
/// document, expense and receipt through one owner-bound revision CAS.
/// </summary>
internal static class CharacterCareerQualityGraph
{
    internal sealed record Projection(
        CharacterCareerQualityDefinition Definition,
        CharacterCareerQualityIdentity Identity,
        CharacterCareerQualityOperation Operation,
        CharacterCareerQualityInstance[] InstancesBefore,
        string Extra, string SourceName, int KarmaBefore,
        string CharacterDigest, string SourceState,
        CharacterCareerQualityEffectProjection Effects,
        string QualityGraphXml);

    internal static bool TryProject(string characterXml, CharacterCareerQualitySource source,
        CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation,
        int? resolvedArmCount, out Projection? projection)
    {
        projection = null;
        if (source is null || identity is null || source.SourceId != identity.SourceId
            || !Enum.IsDefined(operation) || identity.InternalId == Guid.Empty
            || !SourceDigest(source.SourceInputsDigest) || !SourceDigest(source.ProfileInputsDigest)
            || !SourceDigest(source.CustomDataInputsDigest)) return false;
        try
        {
            XElement root = Parse(characterXml);
            if (root.Name != "character" || root.HasAttributes || !bool.TryParse(Scalar(root, "created"), out bool created)
                || !created || !int.TryParse(Scalar(root, "karma"), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int karma)
                || karma is < -CharacterCareerQualityRules.MaximumKarma or > CharacterCareerQualityRules.MaximumKarma)
                return false;
            XElement qualities = Container(root, "qualities", "quality");
            XElement effects = Container(root, "improvements", "improvement");
            var rows = qualities.Elements().Select(row => (Xml: row, Instance: ReadInstance(row))).ToArray();
            if (rows.Length > CharacterCareerReputationProjector.MaximumRowsPerContainer
                || rows.Select(row => row.Instance.Identity.InternalId).Distinct().Count() != rows.Length)
                return false;
            bool acquiring = operation == CharacterCareerQualityOperation.AcquireLevel;
            var target = rows.SingleOrDefault(row => row.Instance.Identity.InternalId == identity.InternalId);
            if (acquiring ? target.Xml is not null : target.Xml is null || target.Instance.Identity != identity)
                return false;
            string extra = acquiring ? string.Empty : target.Instance.Extra;
            string sourceName = acquiring ? string.Empty : target.Instance.SourceName;
            if (!CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(source.RawSourceXml,
                    source.SourceId, identity.InternalId, resolvedArmCount, false, out var definition,
                    out XElement newQuality, out _)
                || source.Name != definition.Name || source.SourceBook != Scalar(newQuality, "source"))
                return false;
            // This compiler does not silently dispose of a prompted/sourced instance.
            if (extra.Length != 0 || sourceName.Length != 0
                || rows.Any(row => row.Instance.Identity.SourceId == identity.SourceId
                    && row.Instance.Type != definition.Type)) return false;
            var matching = rows.Where(row => row.Instance.Identity.SourceId == identity.SourceId
                && row.Instance.Extra == extra && row.Instance.SourceName == sourceName
                && row.Instance.Type == definition.Type).ToArray();
            var removed = new List<string>();
            var added = new List<string>();
            var families = new HashSet<CharacterCareerQualityEffectFamily>();
            var effectsByOwner = effects.Elements().GroupBy(row => Scalar(row, "sourcename"))
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            int firstLevelOwners = 0;
            foreach (var row in matching)
            {
                Guid id = row.Instance.Identity.InternalId;
                XElement[] owned = effectsByOwner.GetValueOrDefault(id.ToString("D")) ?? [];
                if (!ValidateInstance(row.Xml, owned, source, id, resolvedArmCount, out int regularCount))
                    return false;
                if (owned.Length > regularCount && ++firstLevelOwners > 1) return false;
                bool remove = operation == CharacterCareerQualityOperation.RemoveAllLevels
                    || operation == CharacterCareerQualityOperation.RemoveLevel && id == identity.InternalId;
                if (!remove) continue;
                removed.Add(row.Xml.ToString(SaveOptions.DisableFormatting));
                row.Xml.Remove();
                foreach (XElement effect in owned)
                {
                    removed.Add(effect.ToString(SaveOptions.DisableFormatting));
                    effect.Remove();
                }
                if (regularCount > 0) families.Add(CharacterCareerQualityEffectFamily.Bonus);
                if (owned.Length > regularCount) families.Add(CharacterCareerQualityEffectFamily.FirstLevelBonus);
            }
            if (acquiring)
            {
                // Check the entire saved identity space, not just matching qualities.
                if (root.Descendants().Any(node => node.Name.LocalName == "guid"
                        && Guid.TryParse(node.Value, out Guid used) && used == identity.InternalId)
                    || effectsByOwner.ContainsKey(identity.InternalId.ToString("D"))) return false;
                if (!CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(source.RawSourceXml,
                    source.SourceId, identity.InternalId, resolvedArmCount, matching.Length == 0,
                    out _, out newQuality, out var additions)) return false;
                qualities.Add(newQuality);
                added.Add(newQuality.ToString(SaveOptions.DisableFormatting));
                foreach (XElement effect in additions)
                {
                    effects.Add(effect);
                    added.Add(effect.ToString(SaveOptions.DisableFormatting));
                }
                if (newQuality.Element("bonus")!.HasElements) families.Add(CharacterCareerQualityEffectFamily.Bonus);
                if (matching.Length == 0 && newQuality.Element("firstlevelbonus")!.HasElements)
                    families.Add(CharacterCareerQualityEffectFamily.FirstLevelBonus);
            }
            string sourceState = JsonSerializer.Serialize(source);
            string characterDigest = Hash(characterXml);
            string delta = Hash(JsonSerializer.Serialize(new
            {
                Semantics = "sr5-career-quality-graph/v1", characterDigest, sourceState,
                identity, operation, resolvedArmCount, Added = added, Removed = removed
            }));
            // Contract quotes canonicalize instance identity order. Saved XML
            // insertion order is unrelated and must not reject a later level.
            // Sort only the detached projection, never the runner's actual nodes.
            projection = new(definition, identity, operation, matching.Select(row => row.Instance)
                .OrderBy(row => row.Identity.InternalId).ToArray(),
                extra, sourceName, karma, characterDigest, sourceState,
                new(true, families.Order().ToArray(), [], added.Count + removed.Count, delta),
                root.ToString(SaveOptions.DisableFormatting));
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException
            or ArgumentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    internal static bool TryApply(string characterXml, Projection graph, CharacterCareerQualityQuote quote,
        CharacterCareerQualityPlan plan, out string? payload)
    {
        payload = null;
        if (graph is null || !CharacterCareerQualityRules.IsCoherent(quote)
            || !CharacterCareerQualityRules.IsCoherent(plan) || Hash(characterXml) != graph.CharacterDigest
            || quote.Identity != graph.Identity || quote.Operation != graph.Operation || quote.Definition != graph.Definition
            || quote.AvailableKarma != graph.KarmaBefore || quote.Extra != graph.Extra || quote.SourceName != graph.SourceName
            || !Same(quote.InstancesBefore, graph.InstancesBefore) || !Same(quote.Authority.Effects, graph.Effects)
            || quote.SourceRevision != Hash(graph.SourceState)
            || !CharacterCareerQualityRules.TryPlan(quote, plan.ExpectedLogicalRevision,
                plan.ExpectedSourceRevision, plan.ExpectedRuleDigest, plan.ExpectedRuntimeFingerprint,
                plan.ExpectedContentDigest, plan.ExpectedWorkspaceRevision, plan.ExpectedSavedRevision,
                true, false, plan.TransactionId, plan.ExpenseDateLocal, out var expected) || !Same(plan, expected))
            return false;
        try
        {
            XElement root = Parse(graph.QualityGraphXml);
            XElement expenses = Container(root, "expenses", "expense");
            var expenseIds = expenses.Elements().Select(row => Scalar(row, "guid")).ToArray();
            if (expenseIds.Any(id => !Guid.TryParseExact(id, "D", out Guid parsed) || parsed == Guid.Empty)
                || expenseIds.Select(Guid.Parse).Distinct().Count() != expenseIds.Length
                || expenseIds.Any(id => Guid.Parse(id) == plan.TransactionId)) return false;
            Single(root, "karma").Value = plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture);
            if (plan.CreatesExpense)
                expenses.Add(new XElement("expense",
                    new XElement("guid", plan.ExpenseId.ToString("D")),
                    new XElement("date", XmlConvert.ToString(plan.ExpenseDateLocal, XmlDateTimeSerializationMode.Unspecified)),
                    new XElement("amount", plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)),
                    new XElement("reason", plan.ExpenseReason), new XElement("type", plan.ExpenseType),
                    new XElement("refund", plan.ExpenseRefund.ToString(CultureInfo.InvariantCulture)),
                    new XElement("forcecareervisible", plan.ForceCareerVisible.ToString(CultureInfo.InvariantCulture)),
                    new XElement("undo", new XElement("karmatype", plan.KarmaUndoType),
                        new XElement("nuyentype", plan.NuyenUndoType), new XElement("objectid", plan.UndoObjectId),
                        new XElement("qty", plan.UndoQuantity.ToString(CultureInfo.InvariantCulture)),
                        new XElement("extra", plan.UndoExtra))));
            payload = root.ToString(SaveOptions.DisableFormatting);
            return payload.Length <= CharacterCareerReputationProjector.MaximumCharacterXmlLength;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Observe the saved XML, not the requested plan. The executor supplies the
    /// freshly admitted source and owner/revision/runtime binding. This observer
    /// does not certify persistence or provenance; the durable ledger must do so.
    /// </summary>
    internal static bool TryObserve(string characterXml, CharacterCareerQualitySource source,
        CharacterCareerQualityIdentity identity, int? resolvedArmCount,
        CharacterCareerQualityExecutionBinding binding, string ruleDigest, Guid transactionId,
        out CharacterCareerQualityStateObservation? state,
        out CharacterCareerQualityExpenseObservation? expense)
    {
        state = null;
        expense = null;
        if (source is null || identity is null || source.SourceId != identity.SourceId
            || identity.InternalId == Guid.Empty || transactionId == Guid.Empty
            || !SourceDigest(source.SourceInputsDigest) || !SourceDigest(source.ProfileInputsDigest)
            || !SourceDigest(source.CustomDataInputsDigest)) return false;
        try
        {
            XElement root = Parse(characterXml);
            if (root.Name != "character" || root.HasAttributes
                || !bool.TryParse(Scalar(root, "created"), out bool created) || !created
                || !int.TryParse(Scalar(root, "karma"), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int karma)
                || !CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(source.RawSourceXml,
                    source.SourceId, identity.InternalId, resolvedArmCount, false, out var definition,
                    out var expected, out _)
                || source.Name != definition.Name || source.SourceBook != Scalar(expected, "source")) return false;
            var rows = Container(root, "qualities", "quality").Elements()
                .Select(row => (Xml: row, Instance: ReadInstance(row))).ToArray();
            if (rows.Select(row => row.Instance.Identity.InternalId).Distinct().Count() != rows.Length)
                return false;
            var target = rows.SingleOrDefault(row => row.Instance.Identity.InternalId == identity.InternalId);
            if (target.Xml is not null && (target.Instance.Identity != identity
                || target.Instance.Extra.Length != 0 || target.Instance.SourceName.Length != 0)) return false;
            if (rows.Any(row => row.Instance.Identity.SourceId == source.SourceId
                && row.Instance.Type != definition.Type)) return false;
            var matching = rows.Where(row => row.Instance.Identity.SourceId == source.SourceId
                && row.Instance.Type == definition.Type && row.Instance.Extra.Length == 0
                && row.Instance.SourceName.Length == 0).ToArray();
            var effects = Container(root, "improvements", "improvement").Elements()
                .GroupBy(row => Scalar(row, "sourcename"))
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            if (target.Xml is null && effects.ContainsKey(identity.InternalId.ToString("D"))) return false;
            int firstLevelOwners = 0;
            foreach (var row in matching)
            {
                XElement[] owned = effects.GetValueOrDefault(row.Instance.Identity.InternalId.ToString("D")) ?? [];
                if (!ValidateInstance(row.Xml, owned, source, row.Instance.Identity.InternalId,
                        resolvedArmCount, out int regularCount)
                    || owned.Length > regularCount && ++firstLevelOwners > 1) return false;
            }
            var observedExpense = ReadExpense(root, transactionId);
            if (!CharacterCareerQualityRules.TryCreateStateObservation(identity, definition, "", "",
                    matching.Select(row => row.Instance).ToArray(), karma, binding,
                    JsonSerializer.Serialize(source), ruleDigest, out var observedState)) return false;
            state = observedState;
            expense = observedExpense;
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException
            or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            return false;
        }
    }

    private static CharacterCareerQualityExpenseObservation ReadExpense(XElement root, Guid transactionId)
    {
        var expenses = Container(root, "expenses", "expense").Elements()
            .Select(row => (Xml: row, Id: ReadGuid(row, "guid"))).ToArray();
        if (expenses.Select(row => row.Id).Distinct().Count() != expenses.Length)
            throw new InvalidDataException();
        XElement? row = expenses.SingleOrDefault(row => row.Id == transactionId).Xml;
        if (row is null) return new(0, Guid.Empty, DateTime.MinValue, 0, "", "", false, false, "", "", "", 0, "");
        if (row.HasAttributes) throw new InvalidDataException();
        XElement undo = Single(row, "undo");
        if (undo.HasAttributes) throw new InvalidDataException();
        DateTime date = XmlConvert.ToDateTime(Scalar(row, "date"), XmlDateTimeSerializationMode.RoundtripKind);
        if (date.Kind != DateTimeKind.Unspecified) throw new InvalidDataException();
        return new(1, transactionId, date,
            int.Parse(Scalar(row, "amount"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            Scalar(row, "reason"), Scalar(row, "type"), bool.Parse(Scalar(row, "refund")),
            bool.Parse(Scalar(row, "forcecareervisible")), Scalar(undo, "karmatype"), Scalar(undo, "nuyentype"),
            Scalar(undo, "objectid"), decimal.Parse(Scalar(undo, "qty"), NumberStyles.AllowLeadingSign
                | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), Scalar(undo, "extra"));
    }

    private static bool ValidateInstance(XElement row, XElement[] owned, CharacterCareerQualitySource source,
        Guid id, int? armCount, out int regularCount)
    {
        regularCount = 0;
        if (!CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(source.RawSourceXml,
                source.SourceId, id, armCount, false, out _, out var expected, out var regular)
            || !CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(source.RawSourceXml,
                source.SourceId, id, armCount, true, out _, out _, out var first)) return false;
        // Preserve notes, visibility and the original grant origin. Compare every
        // rule-bearing field and owned effect before admitting a mutation/readback.
        foreach (var node in expected.Elements().Where(node => node.Name.LocalName is not
            ("guid" or "notes" or "notesColor" or "print" or "qualitysource" or "contributetolimit")))
            if (!SameElement(Single(row, node.Name.LocalName), node)) return false;
        regularCount = regular.Length;
        return SameEffects(owned, regular) || SameEffects(owned, first);
    }

    private static Guid ReadGuid(XElement row, string name)
        => Guid.TryParseExact(Scalar(row, name), "D", out Guid value) && value != Guid.Empty
            ? value : throw new InvalidDataException();

    private static CharacterCareerQualityInstance ReadInstance(XElement row)
    {
        if (row.HasAttributes || !Enum.TryParse(Scalar(row, "qualitytype"), out CharacterCareerQualityType type)
            || !Enum.IsDefined(type) || type.ToString() != Scalar(row, "qualitytype")
            || !Enum.TryParse(Scalar(row, "qualitysource"), out CharacterCareerQualityOrigin origin)
            || !Enum.IsDefined(origin) || origin.ToString() != Scalar(row, "qualitysource")) throw new InvalidDataException();
        return new(new(ReadGuid(row, "guid"), ReadGuid(row, "sourceid")), Scalar(row, "extra"), Scalar(row, "sourcename"), type, origin);
    }

    private static XElement Parse(string xml)
    {
        if (xml is not { Length: > 0 and <= CharacterCareerReputationProjector.MaximumCharacterXmlLength })
            throw new InvalidDataException();
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength
        });
        return XElement.Load(reader);
    }

    private static XElement Single(XElement parent, string name)
    {
        XElement[] nodes = parent.Elements().Where(node => node.Name.LocalName == name).Take(2).ToArray();
        return nodes.Length == 1 && nodes[0].Name == name ? nodes[0] : throw new InvalidDataException();
    }

    private static string Scalar(XElement parent, string name)
    {
        XElement node = Single(parent, name);
        if (node.HasAttributes || node.HasElements || node.Value != node.Value.Trim()) throw new InvalidDataException();
        return node.Value;
    }

    private static XElement Container(XElement root, string name, string rowName)
    {
        XElement node = Single(root, name);
        if (node.HasAttributes || node.Elements().Any(row => row.Name != rowName)
            || node.Elements().Take(CharacterCareerReputationProjector.MaximumRowsPerContainer + 1).Count()
                > CharacterCareerReputationProjector.MaximumRowsPerContainer
            || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) throw new InvalidDataException();
        return node;
    }

    private static bool SameEffects(IEnumerable<XElement> actual, IEnumerable<XElement> expected)
    {
        string Normalize(XElement value)
        {
            var copy = WithoutContainerIndentation(value);
            copy.Elements("notes").Remove();
            copy.Elements("notesColor").Remove();
            return copy.ToString(SaveOptions.DisableFormatting);
        }
        return actual.Select(Normalize).Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Select(Normalize).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool SameElement(XElement left, XElement right)
        => XNode.DeepEquals(WithoutContainerIndentation(left), WithoutContainerIndentation(right));

    private static XElement WithoutContainerIndentation(XElement value)
    {
        var copy = new XElement(value);
        // Only comparison copies lose layout between child elements. Do not
        // trim scalar values or alter whitespace in the actual saved document.
        foreach (XElement container in copy.DescendantsAndSelf().Where(node => node.HasElements))
            container.Nodes().OfType<XText>().Where(node => string.IsNullOrWhiteSpace(node.Value)).Remove();
        return copy;
    }
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool SourceDigest(string value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;
}
