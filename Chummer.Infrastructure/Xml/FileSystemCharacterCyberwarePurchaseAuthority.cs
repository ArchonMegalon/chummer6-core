using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Fail-closed filesystem authority for the bounded Career purchase/install
/// lane. It does not expose dynamic-rating, generated, child, gear, vehicle,
/// prompt, improvement, custom-directory, or overlay semantics.
/// </summary>
public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority :
    ICharacterCyberwarePurchaseAuthority
{
    private const string EssenceHoleSourceId = "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
    private const string EssenceAntiHoleSourceId = "961eac53-0c43-4b19-8741-2872177a3a4c";

    private static readonly HashSet<string> s_AdmittedSourceFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "category", "ess", "capacity", "avail", "cost",
        "source", "page", "forcegrade", "bannedgrades"
    };

    private readonly IContentOverlayCatalogService _overlays;
    private readonly ICharacterSourceDataResolver _sourceData;
    private readonly Func<string, byte[]> _readAllBytes;

    public FileSystemCharacterCyberwarePurchaseAuthority(
        IContentOverlayCatalogService overlays,
        ICharacterSourceDataResolver sourceData)
        : this(overlays, sourceData, File.ReadAllBytes)
    {
    }

    internal FileSystemCharacterCyberwarePurchaseAuthority(
        IContentOverlayCatalogService overlays,
        ICharacterSourceDataResolver sourceData,
        Func<string, byte[]> readAllBytes)
    {
        _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
        _readAllBytes = readAllBytes ?? throw new ArgumentNullException(nameof(readAllBytes));
    }

    public CharacterCyberwarePurchasePreparation Prepare(string characterXml, long contentRevision)
    {
        string characterDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml);
        var blockers = new List<string>();
        var entries = new List<CharacterCyberwarePurchaseCatalogEntry>();
        var exclusions = new List<CharacterCyberwarePurchaseCatalogExclusion>();
        string profileId = string.Empty;
        string cyberwareDigest = string.Empty;
        decimal nuyen = 0m;
        bool exCon = false;
        int? hole = null;
        int? antiHole = null;
        var settings = new CharacterCyberwarePurchaseSettings(false, false, 0m, false, 0m, 0, false, []);

        XDocument? character = TryParseCharacter(characterXml, blockers);
        XElement? root = character?.Root;
        if (contentRevision < 0)
            blockers.Add("Character content revision must be non-negative.");
        if (root is not null)
        {
            if (!TryReadBoolean(root, "created", out bool created) || !created)
                blockers.Add(CharacterCyberwarePurchaseBlockers.NotCareer);
            if (!TryReadScalar(root, "settings", out profileId) || string.IsNullOrWhiteSpace(profileId))
                blockers.Add("The saved character has no unique settings profile identity.");
            if (!TryReadDecimal(root, "nuyen", out nuyen) || nuyen < 0m)
                blockers.Add("The saved character has no unique non-negative Nuyen balance.");
            if (!TryReadBoolean(root, "excon", out exCon))
                blockers.Add("The saved character has no unique Ex-Con availability state.");
            if (root.Elements("improvements").Take(2).Count() > 1
                || root.Elements("improvements").Any(container => container.Elements().Any()))
            {
                blockers.Add(CharacterCyberwarePurchaseBlockers.ImprovementsUnsupported);
            }
            if (!TryReadEssenceHole(root, EssenceHoleSourceId, out hole)
                || !TryReadEssenceHole(root, EssenceAntiHoleSourceId, out antiHole))
            {
                blockers.Add("Essence Hole saved identity or rating is ambiguous.");
            }
        }

        ContentOverlayCatalog? catalog = null;
        try
        {
            catalog = _overlays.GetCatalog();
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            blockers.Add(CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        }
        if (catalog is null || string.IsNullOrWhiteSpace(catalog.BaseDataPath))
        {
            blockers.Add(CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        }
        else if (catalog.Overlays.Any(overlay => overlay.Enabled))
        {
            blockers.Add(CharacterCyberwarePurchaseBlockers.OverlaysUnsupported);
        }

        XDocument? sourceDocument = null;
        if (catalog is not null && blockers.All(value =>
                !string.Equals(value, CharacterCyberwarePurchaseBlockers.OverlaysUnsupported, StringComparison.Ordinal)))
        {
            string path = Path.Combine(catalog.BaseDataPath, "cyberware.xml");
            try
            {
                byte[] bytes = _readAllBytes(path)
                    ?? throw new IOException("The exact Cyberware source snapshot could not be read.");
                cyberwareDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(
                        cyberwareDigest,
                        CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256,
                        StringComparison.Ordinal))
                {
                    blockers.Add(CharacterCyberwarePurchaseBlockers.PinnedCatalogMismatch);
                }
                else
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    sourceDocument = XDocument.Load(stream, LoadOptions.None);
                }
            }
            catch (Exception exception) when (IsReadFailure(exception) || exception is System.Xml.XmlException)
            {
                blockers.Add(CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
            }
        }

        ICharacterSourceDataContext? context = null;
        if (root is not null)
        {
            try
            {
                context = _sourceData.TryCreateContext(characterXml);
            }
            catch (Exception exception) when (IsReadFailure(exception) || exception is System.Xml.XmlException)
            {
                blockers.Add(CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
            }
            if (context is null)
                blockers.Add(CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        }

        if (catalog is not null
            && sourceDocument?.Root is not null
            && context is not null
            && !string.IsNullOrWhiteSpace(profileId))
        {
            if (!TryResolveSettings(catalog.BaseDataPath, profileId, out settings))
            {
                blockers.Add("The exact Cyberware purchase settings could not be projected.");
            }
            else if (!TryProjectCatalog(
                         sourceDocument,
                         context,
                         settings,
                         exCon,
                         out entries,
                         out exclusions,
                         out string? catalogBlocker))
            {
                blockers.Add(catalogBlocker ?? CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
            }
        }

        if (entries.Count == 0)
            blockers.Add(CharacterCyberwarePurchaseBlockers.CatalogEmpty);
        string[] normalizedBlockers = Normalize(blockers);
        entries.Sort((left, right) => left.SourceId.Value.CompareTo(right.SourceId.Value));
        exclusions.Sort((left, right) => left.SourceId.Value.CompareTo(right.SourceId.Value));
        string catalogDigest = ComputeCatalogDigest(profileId, cyberwareDigest, settings, entries, exclusions);
        return new CharacterCyberwarePurchasePreparation(
            Exact: normalizedBlockers.Length == 0,
            Blockers: normalizedBlockers,
            contentRevision,
            characterDigest,
            catalogDigest,
            profileId,
            cyberwareDigest,
            nuyen,
            exCon,
            hole,
            antiHole,
            settings,
            entries.ToArray(),
            exclusions.ToArray());
    }

    public CharacterCyberwarePurchaseQuote Quote(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection)
        => CharacterCyberwarePurchaseRules.Quote(preparation, selection);

    public CharacterCyberwarePurchaseCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterCyberwarePurchaseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Selection is null)
        {
            return Blocked(characterXml, currentContentRevision, command.NewInstanceId, command.NewExpenseId,
                CharacterCyberwarePurchaseBlockers.IdentityInvalid);
        }
        CharacterCyberwarePurchasePreparation preparation = Prepare(characterXml, currentContentRevision);
        CharacterCyberwarePurchaseQuote quote = Quote(preparation, command.Selection);
        string? blocker = ValidateCommitAuthority(preparation, quote, command, characterXml);
        if (blocker is not null)
            return Blocked(characterXml, currentContentRevision, command.NewInstanceId, command.NewExpenseId, blocker);

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            if (ContainsGuid(root, command.NewInstanceId.Value)
                || ContainsGuid(root, command.NewExpenseId))
            {
                return Blocked(characterXml, currentContentRevision, command.NewInstanceId, command.NewExpenseId,
                    CharacterCyberwarePurchaseBlockers.IdentityInvalid);
            }

            CharacterCyberwarePurchaseCatalogEntry entry = preparation.Entries.Single(candidate =>
                candidate.SourceId == command.Selection.SourceId);
            XElement nuyen = root.Elements("nuyen").Single();
            nuyen.Value = (preparation.AvailableNuyen + quote.NuyenDelta).ToString(CultureInfo.InvariantCulture);
            ApplyEssenceHole(root, quote.NewEssenceHoleRating);

            XElement savedCyberware = CreateSavedCyberware(
                entry,
                quote.GradeName,
                command.Selection,
                command.NewInstanceId);
            XElement savedExpense = CreateExpense(
                entry,
                quote.NuyenDelta,
                command.NewInstanceId,
                command.NewExpenseId,
                command.ExpenseDate);
            XElement cyberwares = GetOrCreateUniqueContainer(root, "cyberwares");
            cyberwares.Add(savedCyberware);
            XElement expenses = GetOrCreateUniqueContainer(root, "expenses");
            expenses.Add(savedExpense);

            string output = document.ToString(SaveOptions.DisableFormatting);
            string outputDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(output);
            decimal holeDelta = ((quote.NewEssenceHoleRating ?? 0) - (preparation.EssenceHoleRating ?? 0)) / 100m;
            var unsignedUndoReceipt = new CharacterCyberwarePurchaseUndoReceipt(
                checked(currentContentRevision + 1),
                outputDigest,
                currentContentRevision,
                preparation.CharacterDigest,
                preparation.AvailableNuyen,
                preparation.EssenceHoleRating,
                preparation.EssenceAntiHoleRating,
                preparation.CatalogDigest,
                quote.QuoteDigest,
                command.Selection.SourceId,
                command.Selection.GradeId,
                command.Selection,
                command.NewInstanceId,
                command.NewExpenseId,
                command.ExpenseDate,
                quote.NuyenDelta,
                ComputeElementDigest(savedCyberware),
                ComputeElementDigest(savedExpense),
                ReceiptDigest: string.Empty);
            CharacterCyberwarePurchaseUndoReceipt undoReceipt = unsignedUndoReceipt with
            {
                ReceiptDigest = CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(unsignedUndoReceipt)
            };
            return new CharacterCyberwarePurchaseCommitResult(
                Committed: true,
                BlockReason: string.Empty,
                currentContentRevision,
                checked(currentContentRevision + 1),
                preparation.CharacterDigest,
                outputDigest,
                output,
                command.NewInstanceId,
                command.NewExpenseId,
                quote.NuyenDelta,
                holeDelta,
                preparation.CatalogDigest,
                quote.QuoteDigest,
                undoReceipt);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException
                                          or ArgumentOutOfRangeException)
        {
            return Blocked(characterXml, currentContentRevision, command.NewInstanceId, command.NewExpenseId,
                "The purchase could not be applied atomically to the exact saved XML shape.");
        }
    }

    public CharacterCyberwarePurchaseCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterCyberwarePurchaseUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CharacterCyberwarePurchaseUndoReceipt? receipt = command.Receipt;
        string digest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml);
        CharacterCyberwareInstanceId instanceId = receipt?.InstanceId ?? new CharacterCyberwareInstanceId(Guid.Empty);
        Guid expenseId = receipt?.ExpenseId ?? Guid.Empty;
        CharacterCyberwarePurchasePreparation preparation = Prepare(characterXml, currentContentRevision);
        if (!preparation.Exact)
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                preparation.Blockers.Count == 0
                    ? CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable
                    : preparation.Blockers[0]);
        if (receipt is null)
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleUndoReceipt);
        if (receipt.ContentRevision != currentContentRevision)
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleRevision);
        if (!FixedEquals(receipt.CharacterDigest, digest)
            || !FixedEquals(receipt.CharacterDigest, preparation.CharacterDigest))
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleCharacter);
        if (!FixedEquals(receipt.CatalogDigest, preparation.CatalogDigest))
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleCatalog);
        if (receipt.Selection is null
            || !CharacterCyberwarePurchaseRules.IsCanonicalDigest(receipt.PreviousCharacterDigest)
            || !FixedEquals(receipt.ReceiptDigest, CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(receipt))
            || !CharacterCyberwarePurchaseRules.IsCanonicalDigest(receipt.QuoteDigest)
            || !CharacterCyberwarePurchaseRules.IsCanonicalDigest(receipt.CyberwareXmlDigest)
            || !CharacterCyberwarePurchaseRules.IsCanonicalDigest(receipt.ExpenseXmlDigest))
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleUndoReceipt);
        }
        Guid source = receipt.SourceId.Value;
        Guid grade = receipt.GradeId.Value;
        Guid instance = receipt.InstanceId.Value;
        if (source == Guid.Empty || grade == Guid.Empty || instance == Guid.Empty || receipt.ExpenseId == Guid.Empty
            || receipt.Selection.SourceId != receipt.SourceId
            || receipt.Selection.GradeId != receipt.GradeId
            || receipt.ExpenseDate.Offset != TimeSpan.Zero
            || source == grade || source == instance || grade == instance
            || receipt.ExpenseId == source || receipt.ExpenseId == grade || receipt.ExpenseId == instance)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.IdentityInvalid);
        }
        if (receipt.PreviousContentRevision != checked(receipt.ContentRevision - 1)
            || receipt.PreviousContentRevision < 0
            || receipt.PreviousAvailableNuyen < 0m)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleUndoReceipt);
        }
        CharacterCyberwarePurchasePreparation purchasePreparation = preparation with
        {
            ContentRevision = receipt.PreviousContentRevision,
            CharacterDigest = receipt.PreviousCharacterDigest,
            AvailableNuyen = receipt.PreviousAvailableNuyen,
            EssenceHoleRating = receipt.PreviousEssenceHoleRating,
            EssenceAntiHoleRating = receipt.PreviousEssenceAntiHoleRating
        };
        CharacterCyberwarePurchaseQuote receiptQuote = Quote(purchasePreparation, receipt.Selection);
        if (!TryAdd(receipt.PreviousAvailableNuyen, receipt.NuyenDelta, out decimal expectedCurrentNuyen)
            || !receiptQuote.Exact
            || !FixedEquals(receiptQuote.QuoteDigest, receipt.QuoteDigest)
            || receiptQuote.NuyenDelta != receipt.NuyenDelta
            || preparation.AvailableNuyen != expectedCurrentNuyen
            || preparation.EssenceHoleRating != NormalizeSavedEssenceHole(receiptQuote.NewEssenceHoleRating)
            || preparation.EssenceAntiHoleRating != receiptQuote.NewEssenceAntiHoleRating)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                CharacterCyberwarePurchaseBlockers.StaleUndoReceipt);
        }

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            XElement[] cyberwares = root.Elements("cyberwares").Take(2).ToArray();
            XElement[] expenses = root.Elements("expenses").Take(2).ToArray();
            if (cyberwares.Length != 1 || expenses.Length != 1)
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                    "The purchase undo containers are absent or ambiguous.");
            XElement[] wareMatches = cyberwares[0].Elements("cyberware").Where(node =>
                    string.Equals(ReadOptionalScalar(node, "guid"), instance.ToString("D"), StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            XElement[] expenseMatches = expenses[0].Elements("expense").Where(node =>
                    string.Equals(ReadOptionalScalar(node, "guid"), expenseId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (wareMatches.Length != 1 || expenseMatches.Length != 1
                || !FixedEquals(ComputeElementDigest(wareMatches.SingleOrDefault()), receipt.CyberwareXmlDigest)
                || !FixedEquals(ComputeElementDigest(expenseMatches.SingleOrDefault()), receipt.ExpenseXmlDigest)
                || !TryReadDecimal(expenseMatches[0], "amount", out decimal expenseAmount)
                || expenseAmount > 0m
                || expenseAmount != receipt.NuyenDelta
                || !string.Equals(ReadOptionalScalar(expenseMatches[0], "type"), "Nuyen", StringComparison.Ordinal)
                || !string.Equals(ReadOptionalScalar(expenseMatches[0].Element("undo"), "nuyentype"), "AddCyberware", StringComparison.Ordinal)
                || !string.Equals(ReadOptionalScalar(expenseMatches[0].Element("undo"), "objectid"), instance.ToString("D"), StringComparison.OrdinalIgnoreCase)
                || !TryReadDecimal(root, "nuyen", out decimal nuyen))
            {
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                    "The exact AddCyberware expense/instance undo binding is absent or ambiguous.");
            }

            CharacterCyberwarePurchaseCatalogEntry[] entries = preparation.Entries.Where(candidate =>
                    candidate.SourceId == receipt.SourceId)
                .Take(2).ToArray();
            CharacterCyberwarePurchaseGrade[] grades = entries.Length == 1
                ? entries[0].Grades.Where(candidate => candidate.Id == receipt.GradeId).Take(2).ToArray()
                : [];
            if (entries.Length != 1 || grades.Length != 1)
            {
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                    "The exact purchased Cyberware source/grade binding is absent or ambiguous.");
            }
            XElement expectedWare = CreateSavedCyberware(
                entries[0],
                grades[0].Name,
                receipt.Selection,
                receipt.InstanceId);
            if (!XNode.DeepEquals(expectedWare, wareMatches[0]))
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                    "The exact purchased Cyberware child shape is altered.");
            XElement expectedExpense = CreateExpense(
                entries[0],
                receipt.NuyenDelta,
                receipt.InstanceId,
                receipt.ExpenseId,
                receipt.ExpenseDate);
            if (!XNode.DeepEquals(expectedExpense, expenseMatches[0]))
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                    "The exact purchased Cyberware expense shape is altered.");

            // Chummer5 undo deletes the ware with increase-Essence-Hole=false,
            // then refunds the negative expense and removes that expense. The
            // already-consumed Essence Hole therefore intentionally stays put.
            wareMatches[0].Remove();
            expenseMatches[0].Remove();
            root.Elements("nuyen").Single().Value = checked(nuyen - expenseAmount).ToString(CultureInfo.InvariantCulture);
            string output = document.ToString(SaveOptions.DisableFormatting);
            return new CharacterCyberwarePurchaseCommitResult(
                Committed: true,
                BlockReason: string.Empty,
                currentContentRevision,
                checked(currentContentRevision + 1),
                digest,
                CharacterCyberwarePurchaseRules.ComputeCharacterDigest(output),
                output,
                instanceId,
                expenseId,
                NuyenDelta: -expenseAmount,
                EssenceHoleDelta: 0m,
                receipt.CatalogDigest,
                receipt.QuoteDigest,
                UndoReceipt: null);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId,
                "The purchase undo could not be applied atomically to the exact saved XML shape.");
        }
    }

    private static string? ValidateCommitAuthority(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseQuote quote,
        CharacterCyberwarePurchaseCommand command,
        string characterXml)
    {
        if (!preparation.Exact)
            return preparation.Blockers.Count == 0
                ? CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable
                : preparation.Blockers[0];
        if (command.ExpectedContentRevision != preparation.ContentRevision)
            return CharacterCyberwarePurchaseBlockers.StaleRevision;
        if (!FixedEquals(command.ExpectedCharacterDigest, preparation.CharacterDigest)
            || !FixedEquals(preparation.CharacterDigest, CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml)))
            return CharacterCyberwarePurchaseBlockers.StaleCharacter;
        if (!FixedEquals(command.ExpectedCatalogDigest, preparation.CatalogDigest))
            return CharacterCyberwarePurchaseBlockers.StaleCatalog;
        if (!quote.Exact)
            return quote.BlockReason;
        if (!FixedEquals(command.ExpectedQuoteDigest, quote.QuoteDigest))
            return CharacterCyberwarePurchaseBlockers.StaleQuote;
        Guid source = command.Selection.SourceId.Value;
        Guid grade = command.Selection.GradeId.Value;
        Guid instance = command.NewInstanceId.Value;
        if (source == Guid.Empty || grade == Guid.Empty || instance == Guid.Empty || command.NewExpenseId == Guid.Empty
            || source == grade || source == instance || grade == instance
            || command.NewExpenseId == source || command.NewExpenseId == grade || command.NewExpenseId == instance
            || command.ExpenseDate.Offset != TimeSpan.Zero)
            return CharacterCyberwarePurchaseBlockers.IdentityInvalid;
        return null;
    }

    private static XDocument? TryParseCharacter(string characterXml, List<string> blockers)
    {
        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null || root.Name.NamespaceName.Length != 0
                             || !string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal)
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

    private static bool TryProjectCatalog(
        XDocument document,
        ICharacterSourceDataContext context,
        CharacterCyberwarePurchaseSettings settings,
        bool exCon,
        out List<CharacterCyberwarePurchaseCatalogEntry> entries,
        out List<CharacterCyberwarePurchaseCatalogExclusion> exclusions,
        out string? blocker)
    {
        entries = [];
        exclusions = [];
        blocker = null;
        XElement? root = document.Root;
        XElement[] gradeContainers = root?.Elements("grades").Take(2).ToArray() ?? [];
        XElement[] wareContainers = root?.Elements("cyberwares").Take(2).ToArray() ?? [];
        XElement[] categoryContainers = root?.Elements("categories").Take(2).ToArray() ?? [];
        if (root is null || root.Name.NamespaceName.Length != 0 || root.Name.LocalName != "chummer"
            || gradeContainers.Length != 1 || wareContainers.Length != 1 || categoryContainers.Length != 1)
        {
            blocker = "The pinned Cyberware catalog containers are absent or ambiguous.";
            return false;
        }

        var rowsById = new Dictionary<Guid, XElement>();
        foreach (XElement row in wareContainers[0].Elements("cyberware"))
        {
            if (!TryReadScalar(row, "id", out string idText)
                || !Guid.TryParseExact(idText, "D", out Guid id)
                || id == Guid.Empty
                || !rowsById.TryAdd(id, row))
            {
                blocker = "The pinned Cyberware catalog contains an invalid or duplicate source ID.";
                return false;
            }
        }
        var gradesById = new Dictionary<Guid, XElement>();
        foreach (XElement row in gradeContainers[0].Elements("grade"))
        {
            if (!TryReadScalar(row, "id", out string idText)
                || !Guid.TryParseExact(idText, "D", out Guid id)
                || id == Guid.Empty
                || !gradesById.TryAdd(id, row))
            {
                blocker = "The pinned Cyberware grades contain an invalid or duplicate source ID.";
                return false;
            }
        }
        HashSet<string> blackMarketCategories = categoryContainers[0].Elements("category")
            .Where(node => string.Equals((string?)node.Attribute("blackmarket"), "Cyberware", StringComparison.Ordinal))
            .Select(node => node.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((Guid id, XElement row) in rowsById.OrderBy(item => item.Key))
        {
            string name = ReadOptionalScalar(row, "name");
            var sourceId = new CharacterCyberwareSourceId(id);
            string? reason = SourceExclusionReason(row, id, name, exCon);
            CharacterCyberwareCommerceSource resolved = CharacterCyberwareCommerceSource.Unavailable;
            if (reason is null
                && (!context.TryResolveCyberwareCommerceSource(id.ToString("D"), name, "Cyberware", out resolved)
                    || resolved.SourceEntryUsesGeneratedOrImprovementSemantics
                    || !string.Equals(resolved.SourceId, id.ToString("D"), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(resolved.Name, name, StringComparison.Ordinal)))
            {
                reason = "Exact enabled-book/profile source resolution failed.";
            }
            if (reason is not null)
            {
                exclusions.Add(new CharacterCyberwarePurchaseCatalogExclusion(sourceId, name, reason));
                continue;
            }

            var grades = new List<CharacterCyberwarePurchaseGrade>();
            foreach (CharacterCyberwareCommerceGradeSource candidate in resolved.Grades)
            {
                if (!Guid.TryParseExact(candidate.Id, "D", out Guid gradeId)
                    || candidate.SpecialSemantics
                    || !gradesById.TryGetValue(gradeId, out XElement? gradeRow)
                    || !TryReadInteger(gradeRow, "avail", out int availabilityModifier)
                    || string.Equals(candidate.Name, "None", StringComparison.Ordinal)
                       && !string.Equals(resolved.ForcedGrade, "None", StringComparison.Ordinal)
                    || settings.BannedGrades.Contains(candidate.Name, StringComparer.Ordinal)
                    || resolved.BannedGrades.Contains(candidate.Name, StringComparer.Ordinal)
                    || resolved.ForcedGrade.Length != 0
                       && !string.Equals(resolved.ForcedGrade, candidate.Name, StringComparison.Ordinal))
                {
                    continue;
                }
                grades.Add(new CharacterCyberwarePurchaseGrade(
                    new CharacterCyberwareGradeId(gradeId),
                    candidate.Name,
                    candidate.CostMultiplier,
                    candidate.EssenceMultiplier,
                    availabilityModifier));
            }
            if (grades.Count == 0)
            {
                exclusions.Add(new CharacterCyberwarePurchaseCatalogExclusion(sourceId, name,
                    "No side-effect-free enabled grade remains."));
                continue;
            }
            grades.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
            string category = ReadOptionalScalar(row, "category");
            entries.Add(new CharacterCyberwarePurchaseCatalogEntry(
                sourceId,
                name,
                category,
                ReadOptionalScalar(row, "ess"),
                ReadOptionalScalar(row, "capacity"),
                ReadOptionalScalar(row, "avail"),
                ReadOptionalScalar(row, "cost"),
                ReadOptionalScalar(row, "source"),
                ReadOptionalScalar(row, "page"),
                blackMarketCategories.Contains(category),
                resolved.ForcedGrade,
                resolved.BannedGrades.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                grades.ToArray()));
        }
        return true;
    }

    private static string? SourceExclusionReason(XElement row, Guid id, string name, bool exCon)
    {
        if (id.ToString("D") is EssenceHoleSourceId or EssenceAntiHoleSourceId)
            return "Essence Hole pseudo-Cyberware is not purchasable in this lane.";
        if (row.Name.NamespaceName.Length != 0 || row.HasAttributes
            || row.Elements().Any(element => element.Name.NamespaceName.Length != 0
                                             || !s_AdmittedSourceFields.Contains(element.Name.LocalName)))
            return "The source row contains generated, child, prompt, matrix, gear, mount, or otherwise unsupported fields.";
        string[] required = ["id", "name", "category", "ess", "capacity", "avail", "cost", "source", "page"];
        if (required.Any(field => !TryReadScalar(row, field, out string value) || string.IsNullOrWhiteSpace(value)))
            return "The source row is missing a unique required scalar.";
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal) || name.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return "The source name is not a stable scalar.";
        if (row.Elements("translate").Count() > 1
            || row.Elements("forcegrade").Count() > 1
            || row.Elements("bannedgrades").Count() > 1
            || row.Elements("bannedgrades").Any(container => container.HasAttributes
                || container.Elements().Any(node => node.Name.LocalName != "grade" || node.HasAttributes || node.HasElements)))
            return "The source grade constraints are ambiguous.";
        if (!decimal.TryParse(ReadOptionalScalar(row, "cost"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cost)
            || !decimal.TryParse(ReadOptionalScalar(row, "ess"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal essence)
            || cost < 0m || essence < 0m)
            return "Dynamic or negative cost/Essence expressions are outside the fixed-value slice.";
        if (!FixedAvailability().IsMatch(ReadOptionalScalar(row, "avail")))
            return "Dynamic availability expressions are outside the fixed-value slice.";
        if (exCon && (ReadOptionalScalar(row, "avail").EndsWith('R')
                      || ReadOptionalScalar(row, "avail").EndsWith('F')))
            return "Ex-Con characters cannot buy restricted or forbidden Cyberware in the bounded lane.";
        return null;
    }

    private static bool TryResolveSettings(
        string baseDataPath,
        string profileId,
        out CharacterCyberwarePurchaseSettings settings)
    {
        settings = new CharacterCyberwarePurchaseSettings(false, false, 0m, false, 0m, 0, false, []);
        try
        {
            XDocument document = XDocument.Load(Path.Combine(baseDataPath, "settings.xml"), LoadOptions.None);
            XElement[] matches = document.Root?.Element("settings")?.Elements("setting").Where(node =>
                    string.Equals(ReadOptionalScalar(node, "id"), profileId, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray() ?? [];
            if (matches.Length != 1
                || !TryReadBoolean(matches[0], "allowcyberwareessdiscounts", out bool allowDiscounts)
                || !TryReadBoolean(matches[0], "multiplyrestrictedcost", out bool multiplyRestricted)
                || !TryReadDecimal(matches[0], "restrictedcostmultiplier", out decimal restrictedMultiplier)
                || !TryReadBoolean(matches[0], "multiplyforbiddencost", out bool multiplyForbidden)
                || !TryReadDecimal(matches[0], "forbiddencostmultiplier", out decimal forbiddenMultiplier)
                || !TryReadBoolean(matches[0], "donotroundessenceinternally", out bool doNotRound)
                || !TryReadScalar(matches[0], "essenceformat", out string essenceFormat)
                || restrictedMultiplier < 0m || forbiddenMultiplier < 0m
                || !TryGetDecimalPlaces(essenceFormat, out int essenceDecimals))
                return false;
            XElement[] bannedContainers = matches[0].Elements("bannedwaregrades").Take(2).ToArray();
            if (bannedContainers.Length > 1
                || bannedContainers.Any(container => container.HasAttributes
                    || container.Elements().Any(node => node.Name.LocalName != "grade"
                                                        || node.HasAttributes
                                                        || node.HasElements)))
                return false;
            string[] bannedGrades = bannedContainers.SingleOrDefault()?.Elements("grade")
                .Select(node => node.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray() ?? [];
            settings = new CharacterCyberwarePurchaseSettings(
                allowDiscounts,
                multiplyRestricted,
                restrictedMultiplier,
                multiplyForbidden,
                forbiddenMultiplier,
                essenceDecimals,
                doNotRound,
                bannedGrades);
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception) || exception is System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool TryGetDecimalPlaces(string format, out int places)
    {
        places = 0;
        int separator = format.LastIndexOf('.');
        if (separator < 0)
            return true;
        string suffix = format[(separator + 1)..];
        if (suffix.Length > 28 || suffix.Any(character => character is not ('0' or '#')))
            return false;
        places = suffix.Length;
        return true;
    }

    private static string ComputeCatalogDigest(
        string profileId,
        string cyberwareDigest,
        CharacterCyberwarePurchaseSettings settings,
        IEnumerable<CharacterCyberwarePurchaseCatalogEntry> entries,
        IEnumerable<CharacterCyberwarePurchaseCatalogExclusion> exclusions)
    {
        var lines = new List<string>
        {
            "career-cyberware-purchase-catalog-v1",
            profileId,
            cyberwareDigest,
            settings.AllowEssenceDiscounts.ToString(CultureInfo.InvariantCulture),
            settings.MultiplyRestrictedCost.ToString(CultureInfo.InvariantCulture),
            settings.RestrictedCostMultiplier.ToString(CultureInfo.InvariantCulture),
            settings.MultiplyForbiddenCost.ToString(CultureInfo.InvariantCulture),
            settings.ForbiddenCostMultiplier.ToString(CultureInfo.InvariantCulture),
            settings.EssenceDecimals.ToString(CultureInfo.InvariantCulture),
            settings.DoNotRoundEssenceInternally.ToString(CultureInfo.InvariantCulture)
        };
        lines.AddRange(settings.BannedGrades.Select(value => $"profile-banned-grade:{value}"));
        lines.AddRange(CharacterCyberwarePurchaseLegacyAuthority.CanonicalInputs);
        foreach (CharacterCyberwarePurchaseCatalogEntry entry in entries.OrderBy(item => item.SourceId.Value))
        {
            lines.Add(string.Join("|",
                "entry", entry.SourceId.Value.ToString("D"), entry.Name, entry.Category,
                entry.EssenceExpression, entry.CapacityExpression, entry.AvailabilityExpression,
                entry.CostExpression, entry.SourceBook, entry.Page,
                entry.BlackMarketEligible.ToString(CultureInfo.InvariantCulture), entry.ForcedGrade,
                string.Join(",", entry.BannedGrades)));
            lines.AddRange(entry.Grades.OrderBy(item => item.Id.Value).Select(grade => string.Join("|",
                "grade", grade.Id.Value.ToString("D"), grade.Name,
                grade.CostMultiplier.ToString(CultureInfo.InvariantCulture),
                grade.EssenceMultiplier.ToString(CultureInfo.InvariantCulture),
                grade.AvailabilityModifier.ToString(CultureInfo.InvariantCulture))));
        }
        lines.AddRange(exclusions.OrderBy(item => item.SourceId.Value).Select(exclusion =>
            string.Join("|", "exclude", exclusion.SourceId.Value.ToString("D"), exclusion.Name, exclusion.Reason)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))))
            .ToLowerInvariant();
    }

    private static XElement CreateSavedCyberware(
        CharacterCyberwarePurchaseCatalogEntry entry,
        string gradeName,
        CharacterCyberwarePurchaseSelection selection,
        CharacterCyberwareInstanceId instanceId)
        => new("cyberware",
            Scalar("guid", instanceId.Value.ToString("D")),
            Scalar("sourceid", entry.SourceId.Value.ToString("D")),
            Scalar("name", entry.Name),
            Scalar("category", entry.Category),
            Scalar("limbslot", string.Empty),
            Scalar("limbslotcount", string.Empty),
            Scalar("inheritattributes", "False"),
            Scalar("ess", entry.EssenceExpression),
            Scalar("capacity", entry.CapacityExpression),
            Scalar("avail", entry.AvailabilityExpression),
            Scalar("cost", selection.FreeCost ? "0" : entry.CostExpression),
            Scalar("weight", string.Empty),
            Scalar("source", entry.SourceBook),
            Scalar("page", entry.Page),
            Scalar("parentid", string.Empty),
            Scalar("hasmodularmount", string.Empty),
            Scalar("plugsintomodularmount", string.Empty),
            Scalar("blocksmounts", string.Empty),
            Scalar("forced", string.Empty),
            Scalar("rating", selection.Rating.ToString(CultureInfo.InvariantCulture)),
            Scalar("minagility", "0"),
            Scalar("minstrength", "0"),
            Scalar("minrating", string.Empty),
            Scalar("maxrating", string.Empty),
            Scalar("ratinglabel", string.Empty),
            Scalar("subsystems", string.Empty),
            Scalar("wirelesson", "False"),
            Scalar("grade", gradeName),
            Scalar("location", string.Empty),
            Scalar("extra", string.Empty),
            Scalar("suite", "False"),
            Scalar("stolen", "False"),
            Scalar("essdiscount", selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture)),
            Scalar("extraessadditivemultiplier", "0"),
            Scalar("extraessmultiplicativemultiplier", "1"),
            Scalar("forcegrade", entry.ForcedGrade),
            Scalar("matrixcmfilled", "0"),
            Scalar("matrixcmbonus", "0"),
            Scalar("prototypetranshuman", "False"),
            Scalar("bonus", string.Empty),
            Scalar("pairbonus", string.Empty),
            Scalar("wirelessbonus", string.Empty),
            Scalar("wirelesspairbonus", string.Empty),
            Scalar("improvementsource", "Cyberware"),
            new XElement("pairinclude", Scalar("name", entry.Name)),
            new XElement("wirelesspairinclude", Scalar("name", entry.Name)),
            Scalar("notes", string.Empty),
            Scalar("notesColor", "Chocolate"),
            Scalar("discountedcost", selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture)),
            Scalar("addtoparentess", "False"),
            Scalar("addtoparentcapacity", "False"),
            Scalar("isgeneware", "False"),
            Scalar("active", "False"),
            Scalar("homenode", "False"),
            Scalar("devicerating", string.Empty),
            Scalar("programlimit", string.Empty),
            Scalar("overclocked", "None"),
            Scalar("canformpersona", string.Empty),
            Scalar("attack", string.Empty),
            Scalar("sleaze", string.Empty),
            Scalar("dataprocessing", string.Empty),
            Scalar("firewall", string.Empty),
            Scalar("attributearray", string.Empty),
            Scalar("modattack", string.Empty),
            Scalar("modsleaze", string.Empty),
            Scalar("moddataprocessing", string.Empty),
            Scalar("modfirewall", string.Empty),
            Scalar("modattributearray", string.Empty),
            Scalar("canswapattributes", "False"),
            Scalar("sortorder", "0"));

    private static XElement CreateExpense(
        CharacterCyberwarePurchaseCatalogEntry entry,
        decimal nuyenDelta,
        CharacterCyberwareInstanceId instanceId,
        Guid expenseId,
        DateTimeOffset expenseDate)
        => new("expense",
            Scalar("guid", expenseId.ToString("D")),
            Scalar("date", expenseDate.UtcDateTime.ToString("s", CultureInfo.InvariantCulture)),
            Scalar("amount", nuyenDelta.ToString(CultureInfo.InvariantCulture)),
            Scalar("reason", $"Purchased Cyberware {entry.Name}"),
            Scalar("type", "Nuyen"),
            Scalar("refund", "False"),
            Scalar("forcecareervisible", "False"),
            new XElement("undo",
                Scalar("karmatype", "ImproveAttribute"),
                Scalar("nuyentype", "AddCyberware"),
                Scalar("objectid", instanceId.Value.ToString("D")),
                Scalar("qty", "0"),
                Scalar("extra", string.Empty)));

    private static void ApplyEssenceHole(XElement root, int? newRating)
    {
        XElement[] containers = root.Elements("cyberwares").Take(2).ToArray();
        if (containers.Length > 1)
            throw new InvalidOperationException("Ambiguous cyberwares container.");
        if (containers.Length == 0 || !newRating.HasValue)
            return;
        XElement[] matches = containers[0].Elements("cyberware").Where(node =>
                string.Equals(ReadOptionalScalar(node, "sourceid"), EssenceHoleSourceId, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("Ambiguous Essence Hole.");
        if (matches.Length == 0)
            return;
        if (newRating.Value == 0)
            matches[0].Remove();
        else
            matches[0].Elements("rating").Single().Value = newRating.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static XElement GetOrCreateUniqueContainer(XElement root, string name)
    {
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"Ambiguous {name} container.");
        if (matches.Length == 1)
            return matches[0];
        var result = new XElement(name);
        root.Add(result);
        return result;
    }

    private static bool TryReadEssenceHole(XElement root, string sourceId, out int? rating)
    {
        rating = null;
        XElement[] containers = root.Elements("cyberwares").Take(2).ToArray();
        if (containers.Length > 1)
            return false;
        if (containers.Length == 0)
            return true;
        XElement[] matches = containers[0].Elements("cyberware").Where(node =>
                string.Equals(ReadOptionalScalar(node, "sourceid"), sourceId, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length > 1)
            return false;
        if (matches.Length == 0)
            return true;
        if (!TryReadInteger(matches[0], "rating", out int value) || value < 0)
            return false;
        rating = value;
        return true;
    }

    private static CharacterCyberwarePurchaseCommitResult Blocked(
        string characterXml,
        long revision,
        CharacterCyberwareInstanceId instanceId,
        Guid expenseId,
        string reason)
    {
        string digest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml);
        return new CharacterCyberwarePurchaseCommitResult(
            Committed: false,
            reason,
            revision,
            revision,
            digest,
            digest,
            characterXml,
            instanceId,
            expenseId,
            NuyenDelta: 0m,
            EssenceHoleDelta: 0m,
            CatalogDigest: string.Empty,
            QuoteDigest: string.Empty,
            UndoReceipt: null);
    }

    private static string ComputeElementDigest(XElement? element)
        => element is null
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    element.ToString(SaveOptions.DisableFormatting))))
                .ToLowerInvariant();

    private static bool ContainsGuid(XElement root, Guid value)
        => root.Descendants().Any(node =>
            node.Name.NamespaceName.Length == 0
            && node.Name.LocalName is "guid" or "id"
            && Guid.TryParse(node.Value, out Guid candidate)
            && candidate == value);

    private static XElement Scalar(string name, string value) => new(name, value);

    private static bool TryReadScalar(XElement? parent, string name, out string value)
    {
        value = string.Empty;
        if (parent is null)
            return false;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].HasAttributes || matches[0].HasElements)
            return false;
        value = matches[0].Value;
        return true;
    }

    private static string ReadOptionalScalar(XElement? parent, string name)
        => TryReadScalar(parent, name, out string value) ? value : string.Empty;

    private static bool TryReadBoolean(XElement parent, string name, out bool value)
    {
        value = false;
        return TryReadScalar(parent, name, out string text) && bool.TryParse(text, out value);
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        value = 0m;
        return TryReadScalar(parent, name, out string text)
               && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInteger(XElement parent, string name, out int value)
    {
        value = 0;
        return TryReadScalar(parent, name, out string text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (!CharacterCyberwarePurchaseRules.IsCanonicalDigest(left)
            || !CharacterCyberwarePurchaseRules.IsCanonicalDigest(right))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right!));
    }

    private static bool TryAdd(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    private static int? NormalizeSavedEssenceHole(int? rating)
        => rating is 0 ? null : rating;

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    [GeneratedRegex("^[0-9]+[RF]?$", RegexOptions.CultureInvariant)]
    private static partial Regex FixedAvailability();
}
