using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Fail-closed Career authority for fixed-value, top-level Bioware whose
/// complete saved shape has no generated assets, children, prompts,
/// requirements, or improvements. All mutations are pure XML results for an
/// external revision CAS; commit-issued receipts provide restart-safe undo.
/// </summary>
public sealed class FileSystemCharacterBiowarePurchaseAuthority :
    ICharacterBiowarePurchaseAuthority
{
    private const string EssenceHoleSourceId = "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
    private const string EssenceAntiHoleSourceId = "961eac53-0c43-4b19-8741-2872177a3a4c";

    private readonly ICharacterSourceDataResolver _sourceData;

    public FileSystemCharacterBiowarePurchaseAuthority(ICharacterSourceDataResolver sourceData)
    {
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
    }

    public CharacterBiowarePurchasePreparation Prepare(string characterXml, long contentRevision)
    {
        string characterDigest = CharacterBiowarePurchaseRules.ComputeCharacterDigest(characterXml);
        var blockers = new List<string>();
        decimal nuyen = 0m;
        bool exCon = false;
        int? hole = null;
        int? antiHole = null;
        CharacterBiowarePurchaseCatalogAuthority catalog = CharacterBiowarePurchaseCatalogAuthority.Unavailable;
        XDocument? character = TryParseCharacter(characterXml, blockers);
        XElement? root = character?.Root;
        if (contentRevision < 0)
            blockers.Add("Character content revision must be non-negative.");
        if (root is not null)
        {
            if (!TryReadBoolean(root, "created", out bool created) || !created)
                blockers.Add(CharacterBiowarePurchaseBlockers.NotCareer);
            if (!TryReadDecimal(root, "nuyen", out nuyen) || nuyen < 0m)
                blockers.Add("The saved character has no unique non-negative Nuyen balance.");
            if (!TryReadBoolean(root, "excon", out exCon))
                blockers.Add("The saved character has no unique Ex-Con availability state.");
            if (root.Elements("improvements").Take(2).Count() > 1
                || root.Elements("improvements").Any(container => container.Elements().Any()))
            {
                blockers.Add(CharacterBiowarePurchaseBlockers.ImprovementsUnsupported);
            }
            if (!TryReadEssenceHole(root, EssenceHoleSourceId, out hole)
                || !TryReadEssenceHole(root, EssenceAntiHoleSourceId, out antiHole))
            {
                blockers.Add("Essence Hole saved identity or rating is ambiguous.");
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
                blockers.Add(CharacterBiowarePurchaseBlockers.SourceAuthorityUnavailable);
            }
            if (context is null
                || !context.TryResolveBiowarePurchaseCatalog(out catalog)
                || !CharacterBiowarePurchaseRules.IsCanonicalDigest(catalog.AuthorityDigest)
                || !FixedEquals(
                    catalog.AuthorityDigest,
                    CharacterBiowarePurchaseRules.ComputeCatalogAuthorityDigest(
                        catalog with { AuthorityDigest = string.Empty })))
            {
                blockers.Add(CharacterBiowarePurchaseBlockers.SourceAuthorityUnavailable);
            }
            else if (!TryReadScalar(root, "settings", out string settingsProfileId)
                     || !string.Equals(
                         settingsProfileId,
                         catalog.Binding.SettingsProfileId,
                         StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add(CharacterBiowarePurchaseBlockers.SourceAuthorityUnavailable);
            }
        }

        if (catalog.Entries.Count == 0)
            blockers.Add(CharacterBiowarePurchaseBlockers.CatalogEmpty);
        string[] normalized = Normalize(blockers);
        return new CharacterBiowarePurchasePreparation(
            normalized.Length == 0,
            normalized,
            contentRevision,
            characterDigest,
            catalog.AuthorityDigest,
            catalog.Binding,
            nuyen,
            exCon,
            hole,
            antiHole,
            catalog.Settings,
            catalog.Entries,
            catalog.Exclusions);
    }

    public CharacterBiowarePurchaseQuote Quote(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseSelection selection)
        => CharacterBiowarePurchaseRules.Quote(preparation, selection);

    public CharacterBiowarePurchaseCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterBiowarePurchaseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Selection is null)
        {
            return Blocked(
                characterXml,
                currentContentRevision,
                command.NewInstanceId,
                command.NewExpenseId,
                CharacterBiowarePurchaseBlockers.IdentityInvalid);
        }
        CharacterBiowarePurchasePreparation preparation = Prepare(characterXml, currentContentRevision);
        CharacterBiowarePurchaseQuote quote = Quote(preparation, command.Selection);
        string? blocker = ValidateCommitAuthority(preparation, quote, command, characterXml);
        if (blocker is not null)
            return Blocked(characterXml, currentContentRevision, command.NewInstanceId, command.NewExpenseId, blocker);

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            if (ContainsGuid(root, command.NewInstanceId.Value)
                || ContainsGuid(root, command.NewExpenseId)
                || ContainsGuid(root, command.Selection.ConfigurationId.Value))
            {
                return Blocked(
                    characterXml,
                    currentContentRevision,
                    command.NewInstanceId,
                    command.NewExpenseId,
                    CharacterBiowarePurchaseBlockers.IdentityInvalid);
            }

            CharacterBiowarePurchaseCatalogEntry entry = preparation.Entries.Single(candidate =>
                candidate.SourceId == command.Selection.SourceId);
            XElement nuyen = root.Elements("nuyen").Single();
            nuyen.Value = checked(preparation.AvailableNuyen + quote.NuyenDelta)
                .ToString(CultureInfo.InvariantCulture);
            ApplyEssenceHole(root, quote.NewEssenceHoleRating);

            XElement savedBioware = CreateSavedBioware(
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
            GetOrCreateUniqueContainer(root, "cyberwares").Add(savedBioware);
            GetOrCreateUniqueContainer(root, "expenses").Add(savedExpense);

            string output = document.ToString(SaveOptions.DisableFormatting);
            string outputDigest = CharacterBiowarePurchaseRules.ComputeCharacterDigest(output);
            decimal holeDelta = ((quote.NewEssenceHoleRating ?? 0) - (preparation.EssenceHoleRating ?? 0)) / 100m;
            var unsignedReceipt = new CharacterBiowarePurchaseUndoReceipt(
                checked(currentContentRevision + 1),
                outputDigest,
                currentContentRevision,
                preparation.CharacterDigest,
                preparation.AvailableNuyen,
                preparation.EssenceHoleRating,
                preparation.EssenceAntiHoleRating,
                preparation.CatalogDigest,
                quote.QuoteId,
                command.Selection.SourceId,
                command.Selection.GradeId,
                command.Selection,
                command.NewInstanceId,
                command.NewExpenseId,
                command.ExpenseDate,
                quote.NuyenDelta,
                ComputeElementDigest(savedBioware),
                ComputeElementDigest(savedExpense),
                string.Empty);
            CharacterBiowarePurchaseUndoReceipt receipt = unsignedReceipt with
            {
                ReceiptDigest = CharacterBiowarePurchaseRules.ComputeUndoReceiptDigest(unsignedReceipt)
            };
            return new CharacterBiowarePurchaseCommitResult(
                true,
                string.Empty,
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
                quote.QuoteId,
                receipt);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException
                                          or ArgumentOutOfRangeException)
        {
            return Blocked(
                characterXml,
                currentContentRevision,
                command.NewInstanceId,
                command.NewExpenseId,
                "The Bioware purchase could not be applied atomically to the exact saved XML shape.");
        }
    }

    public CharacterBiowarePurchaseCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterBiowarePurchaseUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CharacterBiowarePurchaseUndoReceipt? receipt = command.Receipt;
        CharacterBiowareInstanceId instanceId = receipt?.InstanceId
            ?? new CharacterBiowareInstanceId(Guid.Empty);
        Guid expenseId = receipt?.ExpenseId ?? Guid.Empty;
        string digest = CharacterBiowarePurchaseRules.ComputeCharacterDigest(characterXml);
        CharacterBiowarePurchasePreparation preparation = Prepare(characterXml, currentContentRevision);
        if (!preparation.Exact)
        {
            return Blocked(
                characterXml,
                currentContentRevision,
                instanceId,
                expenseId,
                preparation.Blockers.FirstOrDefault() ?? CharacterBiowarePurchaseBlockers.SourceAuthorityUnavailable);
        }
        if (receipt is null)
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleUndoReceipt);
        if (receipt.ContentRevision != currentContentRevision)
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleRevision);
        if (!FixedEquals(receipt.CharacterDigest, digest)
            || !FixedEquals(receipt.CharacterDigest, preparation.CharacterDigest))
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleCharacter);
        if (!FixedEquals(receipt.CatalogDigest, preparation.CatalogDigest))
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleCatalog);
        if (!ValidateReceiptIdentity(receipt)
            || !FixedEquals(receipt.ReceiptDigest, CharacterBiowarePurchaseRules.ComputeUndoReceiptDigest(receipt))
            || !CharacterBiowarePurchaseRules.IsCanonicalDigest(receipt.PreviousCharacterDigest)
            || !CharacterBiowarePurchaseRules.IsCanonicalDigest(receipt.QuoteId.Value)
            || !CharacterBiowarePurchaseRules.IsCanonicalDigest(receipt.BiowareXmlDigest)
            || !CharacterBiowarePurchaseRules.IsCanonicalDigest(receipt.ExpenseXmlDigest))
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleUndoReceipt);
        }
        if (receipt.PreviousContentRevision != checked(receipt.ContentRevision - 1)
            || receipt.PreviousContentRevision < 0
            || receipt.PreviousAvailableNuyen < 0m)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleUndoReceipt);
        }

        CharacterBiowarePurchasePreparation purchasePreparation = preparation with
        {
            ContentRevision = receipt.PreviousContentRevision,
            CharacterDigest = receipt.PreviousCharacterDigest,
            AvailableNuyen = receipt.PreviousAvailableNuyen,
            EssenceHoleRating = receipt.PreviousEssenceHoleRating,
            EssenceAntiHoleRating = receipt.PreviousEssenceAntiHoleRating
        };
        CharacterBiowarePurchaseQuote receiptQuote = Quote(purchasePreparation, receipt.Selection);
        if (!TryAdd(receipt.PreviousAvailableNuyen, receipt.NuyenDelta, out decimal expectedCurrentNuyen)
            || !receiptQuote.Exact
            || !FixedEquals(receiptQuote.QuoteId.Value, receipt.QuoteId.Value)
            || receiptQuote.NuyenDelta != receipt.NuyenDelta
            || preparation.AvailableNuyen != expectedCurrentNuyen
            || preparation.EssenceHoleRating != NormalizeSavedEssenceHole(receiptQuote.NewEssenceHoleRating)
            || preparation.EssenceAntiHoleRating != receiptQuote.NewEssenceAntiHoleRating)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, CharacterBiowarePurchaseBlockers.StaleUndoReceipt);
        }

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            XElement[] wareContainers = root.Elements("cyberwares").Take(2).ToArray();
            XElement[] expenseContainers = root.Elements("expenses").Take(2).ToArray();
            if (wareContainers.Length != 1 || expenseContainers.Length != 1)
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId, "The Bioware undo containers are absent or ambiguous.");
            XElement[] wareMatches = wareContainers[0].Elements("cyberware").Where(node =>
                    string.Equals(
                        ReadOptionalScalar(node, "guid"),
                        instanceId.Value.ToString("D"),
                        StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            XElement[] expenseMatches = expenseContainers[0].Elements("expense").Where(node =>
                    string.Equals(
                        ReadOptionalScalar(node, "guid"),
                        expenseId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (wareMatches.Length != 1 || expenseMatches.Length != 1
                || !FixedEquals(ComputeElementDigest(wareMatches[0]), receipt.BiowareXmlDigest)
                || !FixedEquals(ComputeElementDigest(expenseMatches[0]), receipt.ExpenseXmlDigest)
                || !TryReadDecimal(expenseMatches[0], "amount", out decimal expenseAmount)
                || expenseAmount > 0m
                || expenseAmount != receipt.NuyenDelta
                || !string.Equals(ReadOptionalScalar(expenseMatches[0], "type"), "Nuyen", StringComparison.Ordinal)
                || !string.Equals(ReadOptionalScalar(expenseMatches[0].Element("undo"), "nuyentype"), "AddCyberware", StringComparison.Ordinal)
                || !string.Equals(
                    ReadOptionalScalar(expenseMatches[0].Element("undo"), "objectid"),
                    instanceId.Value.ToString("D"),
                    StringComparison.OrdinalIgnoreCase)
                || !TryReadDecimal(root, "nuyen", out decimal currentNuyen))
            {
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId, "The exact AddCyberware expense/Bioware instance undo binding is absent or ambiguous.");
            }

            CharacterBiowarePurchaseCatalogEntry[] entries = preparation.Entries
                .Where(candidate => candidate.SourceId == receipt.SourceId).Take(2).ToArray();
            CharacterBiowarePurchaseGrade[] grades = entries.Length == 1
                ? entries[0].Grades.Where(candidate => candidate.Id == receipt.GradeId).Take(2).ToArray()
                : [];
            if (entries.Length != 1 || grades.Length != 1)
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId, "The purchased Bioware source/grade binding is absent or ambiguous.");
            XElement expectedWare = CreateSavedBioware(entries[0], grades[0].Name, receipt.Selection, receipt.InstanceId);
            XElement expectedExpense = CreateExpense(entries[0], receipt.NuyenDelta, receipt.InstanceId, receipt.ExpenseId, receipt.ExpenseDate);
            if (!XNode.DeepEquals(expectedWare, wareMatches[0])
                || !XNode.DeepEquals(expectedExpense, expenseMatches[0]))
            {
                return Blocked(characterXml, currentContentRevision, instanceId, expenseId, "The exact purchased Bioware or expense child shape is altered.");
            }

            // Chummer5 AddCyberware undo removes the ware without restoring the
            // already-consumed Essence Hole, then refunds/removes the expense.
            wareMatches[0].Remove();
            expenseMatches[0].Remove();
            root.Elements("nuyen").Single().Value = checked(currentNuyen - expenseAmount)
                .ToString(CultureInfo.InvariantCulture);
            string output = document.ToString(SaveOptions.DisableFormatting);
            return new CharacterBiowarePurchaseCommitResult(
                true,
                string.Empty,
                currentContentRevision,
                checked(currentContentRevision + 1),
                digest,
                CharacterBiowarePurchaseRules.ComputeCharacterDigest(output),
                output,
                instanceId,
                expenseId,
                -expenseAmount,
                0m,
                receipt.CatalogDigest,
                receipt.QuoteId,
                null);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or OverflowException
                                          or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision, instanceId, expenseId, "The Bioware undo could not be applied atomically to the exact saved XML shape.");
        }
    }

    private static string? ValidateCommitAuthority(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseQuote quote,
        CharacterBiowarePurchaseCommand command,
        string characterXml)
    {
        if (!preparation.Exact)
            return preparation.Blockers.FirstOrDefault() ?? CharacterBiowarePurchaseBlockers.SourceAuthorityUnavailable;
        if (command.ExpectedContentRevision != preparation.ContentRevision)
            return CharacterBiowarePurchaseBlockers.StaleRevision;
        if (!FixedEquals(command.ExpectedCharacterDigest, preparation.CharacterDigest)
            || !FixedEquals(preparation.CharacterDigest, CharacterBiowarePurchaseRules.ComputeCharacterDigest(characterXml)))
            return CharacterBiowarePurchaseBlockers.StaleCharacter;
        if (!FixedEquals(command.ExpectedCatalogDigest, preparation.CatalogDigest))
            return CharacterBiowarePurchaseBlockers.StaleCatalog;
        if (!quote.Exact)
            return quote.BlockReason;
        if (!FixedEquals(command.ExpectedQuoteId.Value, quote.QuoteId.Value))
            return CharacterBiowarePurchaseBlockers.StaleQuote;
        Guid source = command.Selection.SourceId.Value;
        Guid grade = command.Selection.GradeId.Value;
        Guid configuration = command.Selection.ConfigurationId.Value;
        Guid instance = command.NewInstanceId.Value;
        Guid expense = command.NewExpenseId;
        if (source == Guid.Empty || grade == Guid.Empty || configuration == Guid.Empty
            || instance == Guid.Empty || expense == Guid.Empty
            || new[] { source, grade, configuration, instance, expense }.Distinct().Count() != 5
            || command.ExpenseDate.Offset != TimeSpan.Zero)
            return CharacterBiowarePurchaseBlockers.IdentityInvalid;
        return null;
    }

    private static bool ValidateReceiptIdentity(CharacterBiowarePurchaseUndoReceipt receipt)
    {
        if (receipt.Selection is null
            || receipt.Selection.SourceId != receipt.SourceId
            || receipt.Selection.GradeId != receipt.GradeId
            || receipt.ExpenseDate.Offset != TimeSpan.Zero)
            return false;
        Guid[] identities =
        [
            receipt.SourceId.Value,
            receipt.GradeId.Value,
            receipt.Selection.ConfigurationId.Value,
            receipt.InstanceId.Value,
            receipt.ExpenseId
        ];
        return identities.All(value => value != Guid.Empty) && identities.Distinct().Count() == identities.Length;
    }

    private static XDocument? TryParseCharacter(string characterXml, ICollection<string> blockers)
    {
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

    private static XElement CreateSavedBioware(
        CharacterBiowarePurchaseCatalogEntry entry,
        string gradeName,
        CharacterBiowarePurchaseSelection selection,
        CharacterBiowareInstanceId instanceId)
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
            Scalar("improvementsource", "Bioware"),
            new XElement("pairinclude", Scalar("name", entry.Name)),
            new XElement("wirelesspairinclude", Scalar("name", entry.Name)),
            Scalar("notes", string.Empty),
            Scalar("notesColor", "Chocolate"),
            Scalar("discountedcost", selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture)),
            Scalar("addtoparentess", "False"),
            Scalar("addtoparentcapacity", "False"),
            Scalar("isgeneware", entry.IsGeneware.ToString(CultureInfo.InvariantCulture)),
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
        CharacterBiowarePurchaseCatalogEntry entry,
        decimal nuyenDelta,
        CharacterBiowareInstanceId instanceId,
        Guid expenseId,
        DateTimeOffset expenseDate)
        => new("expense",
            Scalar("guid", expenseId.ToString("D")),
            Scalar("date", expenseDate.UtcDateTime.ToString("s", CultureInfo.InvariantCulture)),
            Scalar("amount", nuyenDelta.ToString(CultureInfo.InvariantCulture)),
            Scalar("reason", $"Purchased Bioware {entry.Name}"),
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

    private static CharacterBiowarePurchaseCommitResult Blocked(
        string characterXml,
        long revision,
        CharacterBiowareInstanceId instanceId,
        Guid expenseId,
        string reason)
    {
        string digest = CharacterBiowarePurchaseRules.ComputeCharacterDigest(characterXml);
        return new CharacterBiowarePurchaseCommitResult(
            false,
            reason,
            revision,
            revision,
            digest,
            digest,
            characterXml,
            instanceId,
            expenseId,
            0m,
            0m,
            string.Empty,
            new CharacterBiowareQuoteId(string.Empty),
            null);
    }

    private static string ComputeElementDigest(XElement element)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
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
        if (!CharacterBiowarePurchaseRules.IsCanonicalDigest(left)
            || !CharacterBiowarePurchaseRules.IsCanonicalDigest(right))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left!),
            Encoding.ASCII.GetBytes(right!));
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

    private static int? NormalizeSavedEssenceHole(int? rating) => rating is 0 ? null : rating;

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
