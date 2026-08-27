using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Fail-closed SR5 custom-drug recipe authority. Recipe definition is an
/// atomic character mutation with the pinned Chummer5 free initial dose; later
/// quantity purchases intentionally use a separate transaction lane.
/// </summary>
public sealed class FileSystemCharacterCustomDrugAuthority : ICharacterCustomDrugAuthority
{
    private const int MaximumIdempotencyKeyLength = 256;
    private const string DefaultNotesColor = "Chocolate";

    private readonly ICharacterSourceDataResolver _sourceData;

    public FileSystemCharacterCustomDrugAuthority(ICharacterSourceDataResolver sourceData)
    {
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
    }

    public CharacterCustomDrugPreparation Prepare(
        string characterXml,
        long contentRevision,
        CharacterCustomDrugContext context)
    {
        string characterDigest = CharacterCustomDrugRules.ComputeCharacterDigest(characterXml);
        var blockers = new List<string>();
        decimal nuyen = 0m;
        XDocument? document = TryParseCharacter(characterXml, blockers);
        XElement? root = document?.Root;
        if (contentRevision < 0)
            blockers.Add("Character content revision must be non-negative.");
        if (!Enum.IsDefined(context))
            blockers.Add(CharacterCustomDrugBlockers.AuthorityUnavailable);
        if (root is not null)
        {
            if (!TryReadBoolean(root, "created", out bool created))
                blockers.Add("The saved character has no unique creation state.");
            else if (context == CharacterCustomDrugContext.Career && !created)
                blockers.Add(CharacterCustomDrugBlockers.NotCareer);
            else if (context == CharacterCustomDrugContext.Creation && created)
                blockers.Add(CharacterCustomDrugBlockers.NotCreation);
            if (!TryReadDecimal(root, "nuyen", out nuyen) || nuyen < 0m)
                blockers.Add("The saved character has no unique non-negative Nuyen balance.");
            if (context == CharacterCustomDrugContext.Career
                && root.Elements("drugs").Take(2).Count() != 1)
                blockers.Add("The saved custom-drug container is ambiguous.");
        }

        CharacterCustomDrugCatalogAuthority authority = CharacterCustomDrugCatalogAuthority.Unavailable;
        if (root is not null)
        {
            try
            {
                ICharacterSourceDataContext? sourceContext = _sourceData.TryCreateContext(characterXml);
                if (sourceContext is null || !sourceContext.TryResolveCustomDrugCatalog(out authority))
                    blockers.Add(CharacterCustomDrugBlockers.AuthorityUnavailable);
            }
            catch (Exception exception) when (IsReadFailure(exception) || exception is System.Xml.XmlException)
            {
                blockers.Add(CharacterCustomDrugBlockers.AuthorityUnavailable);
            }
        }

        CharacterCustomDrugPreparation preparation = CharacterCustomDrugRules.BindPreparation(
            authority,
            context,
            CharacterCustomDrugQuotePurpose.RecipeDefinition,
            Math.Max(0, contentRevision),
            characterDigest,
            Math.Max(0m, nuyen));
        string[] normalized = Normalize(blockers.Concat(preparation.Blockers));
        return preparation with
        {
            Exact = normalized.Length == 0 && preparation.Exact,
            Blockers = normalized
        };
    }

    public CharacterCustomDrugQuote Quote(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
        => CharacterCustomDrugRules.Quote(preparation, selection);

    public CharacterCustomDrugCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (context != CharacterCustomDrugContext.Career)
            return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.CreationMutationRequiresFinalizer);
        CharacterCustomDrugCommitResult recovered = LookupReceipt(
            characterXml,
            currentContentRevision,
            context,
            command);
        if (recovered.AlreadyCommitted)
            return recovered;

        CharacterCustomDrugPreparation preparation = Prepare(characterXml, currentContentRevision, context);
        CharacterCustomDrugQuote quote = command.Selection is null
            ? BlockedQuote(CharacterCustomDrugBlockers.InvalidIdentity)
            : Quote(preparation, command.Selection);
        string? blocker = ValidateCommit(preparation, quote, command, characterXml);
        if (blocker is not null)
            return Blocked(characterXml, currentContentRevision, blocker);

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            Guid[] identities = NewIdentities(command);
            if (identities.Any(identity => ContainsInstanceIdentity(root, identity)))
                return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.InvalidIdentity);

            XElement drug = CreateSavedDrug(preparation, quote, command);
            XElement drugs = root.Elements("drugs").Single();
            drugs.Add(drug);

            string output = document.ToString(SaveOptions.DisableFormatting);
            string outputDigest = CharacterCustomDrugRules.ComputeCharacterDigest(output);
            string commandDigest = CharacterCustomDrugRules.ComputeCommandDigest(command);
            var unsigned = new CharacterCustomDrugCommitReceipt(
                currentContentRevision,
                checked(currentContentRevision + 1),
                preparation.CharacterDigest,
                outputDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                quote.QuoteDigest,
                commandDigest,
                CharacterCustomDrugRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
                command.NewDrugInstanceId,
                command.NewComponentInstanceIds.ToArray(),
                ComputeElementDigest(drug),
                ReceiptDigest: string.Empty);
            CharacterCustomDrugCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterCustomDrugRules.ComputeReceiptDigest(unsigned)
            };
            return new CharacterCustomDrugCommitResult(
                Committed: true,
                AlreadyCommitted: false,
                BlockReason: string.Empty,
                currentContentRevision,
                checked(currentContentRevision + 1),
                preparation.CharacterDigest,
                outputDigest,
                output,
                receipt);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or ArgumentOutOfRangeException
                                          or System.Xml.XmlException)
        {
            return Blocked(
                characterXml,
                currentContentRevision,
                "The custom-drug recipe could not be applied atomically to the exact saved XML shape.");
        }
    }

    public CharacterCustomDrugCommitResult LookupReceipt(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (context != CharacterCustomDrugContext.Career
            || command.Selection is null
            || command.ExpectedContentRevision < 0
            || command.ExpectedContentRevision == long.MaxValue
            || currentContentRevision != command.ExpectedContentRevision + 1
            || !IsValidIdempotencyKey(command.IdempotencyKey)
            || !TryValidateIdentities(command, out _))
        {
            return Blocked(characterXml, currentContentRevision, string.Empty);
        }

        CharacterCustomDrugPreparation current = Prepare(characterXml, currentContentRevision, context);
        if (!current.Exact
            || !FixedEquals(current.CatalogDigest, command.ExpectedCatalogDigest)
            || !FixedEquals(current.RulesDigest, command.ExpectedRulesDigest))
        {
            return Blocked(characterXml, currentContentRevision, string.Empty);
        }
        CharacterCustomDrugPreparation previous = current with
        {
            ContentRevision = command.ExpectedContentRevision,
            CharacterDigest = command.ExpectedCharacterDigest
        };
        CharacterCustomDrugQuote quote = Quote(previous, command.Selection);
        if (!quote.Exact || !FixedEquals(quote.QuoteDigest, command.ExpectedQuoteDigest))
            return Blocked(characterXml, currentContentRevision, string.Empty);

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            XElement[] containers = root.Elements("drugs").Take(2).ToArray();
            if (containers.Length != 1)
                return Blocked(characterXml, currentContentRevision, string.Empty);
            string instance = command.NewDrugInstanceId.Value.ToString("D");
            XElement[] matches = containers[0].Elements("drug").Where(node =>
                    string.Equals(ReadOptionalScalar(node, "guid"), instance, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length != 1)
                return Blocked(characterXml, currentContentRevision, string.Empty);
            XElement expected = CreateSavedDrug(previous, quote, command);
            if (!XNode.DeepEquals(expected, matches[0]))
                return Blocked(characterXml, currentContentRevision, string.Empty);

            string digest = CharacterCustomDrugRules.ComputeCharacterDigest(characterXml);
            string commandDigest = CharacterCustomDrugRules.ComputeCommandDigest(command);
            var unsigned = new CharacterCustomDrugCommitReceipt(
                command.ExpectedContentRevision,
                currentContentRevision,
                command.ExpectedCharacterDigest,
                digest,
                current.CatalogDigest,
                current.RulesDigest,
                quote.QuoteDigest,
                commandDigest,
                CharacterCustomDrugRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
                command.NewDrugInstanceId,
                command.NewComponentInstanceIds.ToArray(),
                ComputeElementDigest(matches[0]),
                ReceiptDigest: string.Empty);
            CharacterCustomDrugCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterCustomDrugRules.ComputeReceiptDigest(unsigned)
            };
            return new CharacterCustomDrugCommitResult(
                Committed: true,
                AlreadyCommitted: true,
                BlockReason: string.Empty,
                command.ExpectedContentRevision,
                currentContentRevision,
                command.ExpectedCharacterDigest,
                digest,
                characterXml,
                receipt);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision, string.Empty);
        }
    }

    public CharacterCustomDrugCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (context != CharacterCustomDrugContext.Career)
            return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.CreationMutationRequiresFinalizer);
        CharacterCustomDrugCommitReceipt? receipt = command.Receipt;
        CharacterCustomDrugPreparation preparation = Prepare(characterXml, currentContentRevision, context);
        string digest = CharacterCustomDrugRules.ComputeCharacterDigest(characterXml);
        if (!preparation.Exact)
            return Blocked(characterXml, currentContentRevision,
                preparation.Blockers.FirstOrDefault() ?? CharacterCustomDrugBlockers.AuthorityUnavailable);
        if (receipt is null
            || receipt.ContentRevision != currentContentRevision
            || receipt.PreviousContentRevision != checked(currentContentRevision - 1)
            || !FixedEquals(receipt.CharacterDigest, digest)
            || !FixedEquals(receipt.CharacterDigest, preparation.CharacterDigest)
            || !FixedEquals(receipt.CatalogDigest, preparation.CatalogDigest)
            || !FixedEquals(receipt.RulesDigest, preparation.RulesDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(receipt.PreviousCharacterDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(receipt.CommandDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(receipt.IdempotencyKeyDigest)
            || !CharacterCustomDrugRules.IsCanonicalDigest(receipt.DrugXmlDigest)
            || !FixedEquals(receipt.ReceiptDigest, CharacterCustomDrugRules.ComputeReceiptDigest(receipt)))
        {
            return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleReceipt);
        }

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            XElement[] containers = root.Elements("drugs").Take(2).ToArray();
            if (containers.Length != 1)
                return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleReceipt);
            string instance = receipt.DrugInstanceId.Value.ToString("D");
            XElement[] matches = containers[0].Elements("drug").Where(node =>
                    string.Equals(ReadOptionalScalar(node, "guid"), instance, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length != 1
                || !FixedEquals(ComputeElementDigest(matches[0]), receipt.DrugXmlDigest))
            {
                return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleReceipt);
            }
            Guid[] savedComponentIds = matches[0]
                .Element("drugcomponents")?
                .Elements("drugcomponent")
                .Select(node => Guid.TryParse(ReadOptionalScalar(node, "guid"), out Guid value) ? value : Guid.Empty)
                .ToArray() ?? [];
            if (!savedComponentIds.SequenceEqual(receipt.ComponentInstanceIds))
                return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleReceipt);

            matches[0].Remove();
            string output = document.ToString(SaveOptions.DisableFormatting);
            return new CharacterCustomDrugCommitResult(
                Committed: true,
                AlreadyCommitted: false,
                BlockReason: string.Empty,
                currentContentRevision,
                checked(currentContentRevision + 1),
                digest,
                CharacterCustomDrugRules.ComputeCharacterDigest(output),
                output,
                Receipt: null);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision,
                "The custom-drug recipe undo could not be applied atomically to the exact saved XML shape.");
        }
    }

    private static string? ValidateCommit(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugQuote quote,
        CharacterCustomDrugCommitCommand command,
        string characterXml)
    {
        if (!preparation.Exact)
            return preparation.Blockers.FirstOrDefault() ?? CharacterCustomDrugBlockers.AuthorityUnavailable;
        if (preparation.Purpose != CharacterCustomDrugQuotePurpose.RecipeDefinition)
            return CharacterCustomDrugBlockers.AuthorityUnavailable;
        if (command.ExpectedContentRevision != preparation.ContentRevision)
            return CharacterCustomDrugBlockers.StaleRevision;
        if (!FixedEquals(command.ExpectedCharacterDigest, preparation.CharacterDigest)
            || !FixedEquals(preparation.CharacterDigest, CharacterCustomDrugRules.ComputeCharacterDigest(characterXml)))
            return CharacterCustomDrugBlockers.StaleCharacter;
        if (!FixedEquals(command.ExpectedCatalogDigest, preparation.CatalogDigest))
            return CharacterCustomDrugBlockers.StaleCatalog;
        if (!FixedEquals(command.ExpectedRulesDigest, preparation.RulesDigest))
            return CharacterCustomDrugBlockers.StaleRules;
        if (!quote.Exact)
            return quote.BlockReason;
        if (!FixedEquals(command.ExpectedQuoteDigest, quote.QuoteDigest))
            return CharacterCustomDrugBlockers.StaleQuote;
        if (!IsValidIdempotencyKey(command.IdempotencyKey))
            return CharacterCustomDrugBlockers.InvalidIdempotencyKey;
        if (!TryValidateIdentities(command, out string? blocker))
            return blocker;
        return null;
    }

    private static bool TryValidateIdentities(
        CharacterCustomDrugCommitCommand command,
        out string? blocker)
    {
        blocker = CharacterCustomDrugBlockers.InvalidIdentity;
        if (command.Selection is null
            || command.Selection.Components is null
            || command.NewComponentInstanceIds is null
            || command.NewDrugInstanceId.Value == Guid.Empty
            || command.NewComponentInstanceIds.Count != command.Selection.Components.Count
            || command.NewComponentInstanceIds.Any(value => value == Guid.Empty))
        {
            return false;
        }
        Guid[] newIdentities = NewIdentities(command);
        Guid[] authorityIdentities = [
            command.Selection.GradeId.Value,
            .. command.Selection.Components.Select(value => value.ComponentId.Value).Distinct()
        ];
        if (newIdentities.Distinct().Count() != newIdentities.Length
            || authorityIdentities.Any(value => value == Guid.Empty)
            || newIdentities.Intersect(authorityIdentities).Any())
            return false;
        blocker = null;
        return true;
    }

    private static Guid[] NewIdentities(CharacterCustomDrugCommitCommand command)
        => [command.NewDrugInstanceId.Value, .. command.NewComponentInstanceIds];

    private static XElement CreateSavedDrug(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugQuote quote,
        CharacterCustomDrugCommitCommand command)
    {
        CharacterCustomDrugGrade grade = preparation.Grades.Single(value => value.Id == quote.GradeId);
        var sourceById = preparation.Components.ToDictionary(value => value.Id);
        var components = new XElement("drugcomponents");
        for (int index = 0; index < command.Selection.Components.Count; index++)
        {
            CharacterCustomDrugComponentSelection selected = command.Selection.Components[index];
            CharacterCustomDrugComponentSource source = sourceById[selected.ComponentId];
            components.Add(CreateSavedComponent(source, selected.Level, command.NewComponentInstanceIds[index]));
        }
        return new XElement("drug",
            new XElement("sourceid", Guid.Empty.ToString("D")),
            new XElement("guid", command.NewDrugInstanceId.Value.ToString("D")),
            new XElement("name", quote.Name),
            new XElement("category", "Custom Drug"),
            new XElement("quantity", "1"),
            components,
            new XElement("availability", "0"),
            new XElement("grade", grade.Name),
            new XElement("sortorder", "0"),
            new XElement("stolen", bool.FalseString),
            new XElement("source", string.Empty),
            new XElement("page", string.Empty),
            new XElement("notes", string.Empty),
            new XElement("notesColor", DefaultNotesColor));
    }

    private static XElement CreateSavedComponent(
        CharacterCustomDrugComponentSource source,
        int selectedLevel,
        Guid instanceId)
    {
        var effects = new XElement("effects");
        foreach (CharacterCustomDrugEffectLevel effect in source.Effects.OrderBy(value => value.Level))
        {
            var saved = new XElement("effect", new XElement("level", effect.Level.ToString(CultureInfo.InvariantCulture)));
            foreach (CharacterCustomDrugAttributeEffect value in effect.Attributes)
                saved.Add(new XElement("attribute", new XElement("name", value.Attribute), new XElement("value", value.Value.ToString(CultureInfo.InvariantCulture))));
            foreach (CharacterCustomDrugLimitEffect value in effect.Limits)
                saved.Add(new XElement("limit", new XElement("name", value.Limit), new XElement("value", value.Value.ToString(CultureInfo.InvariantCulture))));
            foreach (CharacterCustomDrugQualityEffect value in effect.Qualities)
            {
                saved.Add(new XElement("quality",
                    value.Rating == 0 ? null : new XAttribute("rating", value.Rating.ToString(CultureInfo.InvariantCulture)),
                    value.Name));
            }
            foreach (string value in effect.Information)
                saved.Add(new XElement("info", value));
            AddNonZero(saved, "initiative", effect.Initiative);
            AddNonZero(saved, "initiativedice", effect.InitiativeDice);
            AddNonZero(saved, "duration", effect.Duration);
            AddNonZero(saved, "speed", effect.Speed);
            AddNonZero(saved, "crashdamage", effect.CrashDamage);
            effects.Add(saved);
        }

        var component = new XElement("drugcomponent",
            new XElement("sourceid", source.Id.Value.ToString("D")),
            new XElement("guid", instanceId.ToString("D")),
            new XElement("name", source.Name),
            new XElement("category", source.Category.ToString()),
            effects,
            new XElement("availability", FormatAvailability(source)),
            new XElement("cost", source.CostPerLevel.ToString(CultureInfo.InvariantCulture)),
            new XElement("level", selectedLevel.ToString(CultureInfo.InvariantCulture)),
            new XElement("limit", source.Limit.ToString(CultureInfo.InvariantCulture)));
        if (source.AddictionRating != 0)
            component.Add(new XElement("rating", source.AddictionRating.ToString(CultureInfo.InvariantCulture)));
        if (source.AddictionThreshold != 0)
            component.Add(new XElement("threshold", source.AddictionThreshold.ToString(CultureInfo.InvariantCulture)));
        component.Add(new XElement("source", source.SourceBook));
        component.Add(new XElement("page", source.Page));
        return component;
    }

    private static string FormatAvailability(CharacterCustomDrugComponentSource source)
        => $"+{source.AvailabilityModifier.ToString(CultureInfo.InvariantCulture)}{source.Legality switch
        {
            CharacterCustomDrugLegality.Restricted => "R",
            CharacterCustomDrugLegality.Forbidden => "F",
            _ => string.Empty
        }}";

    private static void AddNonZero(XElement parent, string name, int value)
    {
        if (value != 0)
            parent.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
    }

    private static XDocument? TryParseCharacter(string characterXml, ICollection<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(characterXml))
        {
            blockers.Add("The saved character XML is empty.");
            return null;
        }
        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null
                || root.Name.NamespaceName.Length != 0
                || root.Name.LocalName != "character"
                || root.HasAttributes)
            {
                blockers.Add("The saved character XML root is unsupported.");
                return null;
            }
            return document;
        }
        catch (System.Xml.XmlException)
        {
            blockers.Add("The saved character XML is malformed.");
            return null;
        }
    }

    private static bool TryReadBoolean(XElement root, string name, out bool value)
    {
        value = false;
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        return matches.Length == 1 && bool.TryParse(matches[0].Value, out value);
    }

    private static bool TryReadDecimal(XElement root, string name, out decimal value)
    {
        value = 0m;
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        return matches.Length == 1
               && decimal.TryParse(matches[0].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string ReadOptionalScalar(XElement? parent, string name)
    {
        XElement[] matches = parent?.Elements(name).Take(2).ToArray() ?? [];
        return matches.Length == 1 && !matches[0].HasElements ? matches[0].Value : string.Empty;
    }

    private static bool ContainsInstanceIdentity(XElement root, Guid identity)
    {
        string value = identity.ToString("D");
        return root.Descendants().Any(node =>
            node.Name.LocalName is "guid" or "internalid"
            && !node.HasElements
            && string.Equals(node.Value.Trim(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidIdempotencyKey(string? value)
        => value is { Length: > 0 and <= MaximumIdempotencyKeyLength }
           && value.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static string ComputeElementDigest(XElement element)
        => CharacterCustomDrugRules.ComputeCharacterDigest(element.ToString(SaveOptions.DisableFormatting));

    private static CharacterCustomDrugQuote BlockedQuote(string reason)
        => new(
            Exact: false,
            reason,
            Name: string.Empty,
            new CharacterCustomDrugGradeId(Guid.Empty),
            GradeName: string.Empty,
            Quantity: 0m,
            ComponentCost: 0m,
            UnitCost: 0m,
            ChargedCost: 0m,
            NuyenDelta: 0m,
            Availability: 0,
            CharacterCustomDrugLegality.Legal,
            AddictionRating: 0,
            AddictionThreshold: 0,
            new CharacterCustomDrugAggregateEffects([], [], [], [], 0, 0, 0, 0, 0),
            Components: [],
            QuoteDigest: string.Empty);

    private static CharacterCustomDrugCommitResult Blocked(
        string characterXml,
        long currentContentRevision,
        string? reason)
    {
        string digest = CharacterCustomDrugRules.ComputeCharacterDigest(characterXml);
        return new CharacterCustomDrugCommitResult(
            Committed: false,
            AlreadyCommitted: false,
            BlockReason: reason ?? string.Empty,
            currentContentRevision,
            currentContentRevision,
            digest,
            digest,
            characterXml,
            Receipt: null);
    }

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null || left.Length != right.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private static bool IsReadFailure(Exception exception)
        => exception is IOException
           or UnauthorizedAccessException
           or NotSupportedException
           or System.Security.SecurityException;
}
