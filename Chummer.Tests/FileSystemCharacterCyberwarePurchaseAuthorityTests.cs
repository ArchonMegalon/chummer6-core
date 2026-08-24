using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class FileSystemCharacterCyberwarePurchaseAuthorityTests
{
    private const string ProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string SimrigId = "ed88f785-7d61-43ec-b4ed-0ebb94736f5e";
    private const string SkilljackId = "d31497dd-2be9-4b8a-808f-3ced36287c0c";
    private const string StandardGradeId = "23382221-fd16-44ec-8da7-9b935ed2c1ee";
    private const string HoleSourceId = "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
    private static readonly Guid s_InstanceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid s_ExpenseId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void Pinned_catalog_lists_only_deterministically_admitted_rows()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(CharacterXml(), 42);

        Assert.IsTrue(preparation.Exact, string.Join("; ", preparation.Blockers));
        Assert.AreEqual(CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256, preparation.CyberwareXmlDigest);
        Assert.AreEqual(40, CharacterCyberwarePurchaseLegacyAuthority.Commit.Length);
        Assert.AreEqual(40, CharacterCyberwarePurchaseLegacyAuthority.Tree.Length);
        Assert.AreEqual(64, preparation.CatalogDigest.Length);
        CharacterCyberwarePurchaseCatalogEntry simrig = preparation.Entries.Single(entry =>
            entry.SourceId.Value == Guid.Parse(SimrigId));
        Assert.AreEqual("Simrig", simrig.Name);
        Assert.AreEqual("4000", simrig.CostExpression);
        Assert.AreEqual("0.2", simrig.EssenceExpression);
        Assert.IsTrue(simrig.BlackMarketEligible);
        Assert.IsTrue(simrig.Grades.Any(grade => grade.Id.Value == Guid.Parse(StandardGradeId)));
        Assert.IsFalse(simrig.Grades.Any(grade => string.Equals(grade.Name, "Betaware", StringComparison.Ordinal)));
        Assert.IsTrue(preparation.Exclusions.Any(exclusion =>
            exclusion.SourceId.Value == Guid.Parse(SkilljackId)
            && exclusion.Reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase)));
        Assert.AreNotEqual(simrig.SourceId.Value, simrig.Grades[0].Id.Value);

        string sourcePath = Path.Combine(DataPath(), "cyberware.xml");
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        Assert.AreEqual(CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256, actual);
        CollectionAssert.Contains(
            CharacterCyberwarePurchaseLegacyAuthority.CanonicalInputs.ToArray(),
            $"Chummer/Forms/Character Forms/CharacterCareer.cs:{CharacterCyberwarePurchaseLegacyAuthority.CharacterCareerSha256}");
        CollectionAssert.Contains(
            CharacterCyberwarePurchaseLegacyAuthority.CanonicalInputs.ToArray(),
            $"Chummer/Backend/Characters/Character.cs:{CharacterCyberwarePurchaseLegacyAuthority.CharacterSha256}");
        CollectionAssert.Contains(
            CharacterCyberwarePurchaseLegacyAuthority.CanonicalInputs.ToArray(),
            $"Chummer/Backend/Static/Extensions/DecimalExtensions.cs:{CharacterCyberwarePurchaseLegacyAuthority.DecimalExtensionsSha256}");
    }

    [TestMethod]
    public void Quote_commit_restart_and_undo_preserve_cost_hole_nuyen_expense_and_instance_identity()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(before, 42);
        CharacterCyberwarePurchaseSelection selection = SimrigSelection(
            blackMarket: true,
            markup: 10m);
        CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selection);

        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(4_000m, quote.BaseCost);
        Assert.AreEqual(3_960m, quote.ChargedCost);
        Assert.AreEqual(-3_960m, quote.NuyenDelta);
        Assert.AreEqual(0.2m, quote.InstalledEssence);
        Assert.AreEqual(30, quote.NewEssenceHoleRating);
        Assert.AreEqual(64, quote.QuoteDigest.Length);

        CharacterCyberwarePurchaseCommitResult committed = authority.Commit(
            before,
            42,
            Command(preparation, quote, selection));
        Assert.IsTrue(committed.Committed, committed.BlockReason);
        Assert.AreEqual(43L, committed.NewContentRevision);
        Assert.AreEqual(-3_960m, committed.NuyenDelta);
        Assert.AreEqual(-0.2m, committed.EssenceHoleDelta);
        Assert.AreNotEqual(committed.PreviousCharacterDigest, committed.NewCharacterDigest);
        Assert.IsNotNull(committed.UndoReceipt);
        CharacterCyberwarePurchaseUndoReceipt undoReceipt = committed.UndoReceipt!;
        Assert.AreEqual(committed.NewContentRevision, undoReceipt.ContentRevision);
        Assert.AreEqual(committed.NewCharacterDigest, undoReceipt.CharacterDigest);
        Assert.AreEqual(committed.PreviousContentRevision, undoReceipt.PreviousContentRevision);
        Assert.AreEqual(committed.PreviousCharacterDigest, undoReceipt.PreviousCharacterDigest);
        Assert.AreEqual(10_000m, undoReceipt.PreviousAvailableNuyen);
        Assert.AreEqual(50, undoReceipt.PreviousEssenceHoleRating);
        Assert.IsNull(undoReceipt.PreviousEssenceAntiHoleRating);
        Assert.AreEqual(preparation.CatalogDigest, undoReceipt.CatalogDigest);
        Assert.AreEqual(s_InstanceId, undoReceipt.InstanceId.Value);
        Assert.AreEqual(s_ExpenseId, undoReceipt.ExpenseId);

        XDocument saved = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement root = saved.Root!;
        Assert.AreEqual("6040.00", root.Element("nuyen")!.Value);
        XElement ware = root.Element("cyberwares")!.Elements("cyberware").Single(node =>
            string.Equals(node.Element("guid")?.Value, s_InstanceId.ToString("D"), StringComparison.Ordinal));
        AssertSavedSimrigExactly(ware);
        XElement expense = root.Element("expenses")!.Elements("expense").Single();
        Assert.AreEqual(s_ExpenseId.ToString("D"), expense.Element("guid")!.Value);
        Assert.AreEqual("-3960.00", expense.Element("amount")!.Value);
        Assert.AreEqual("Nuyen", expense.Element("type")!.Value);
        Assert.AreEqual("AddCyberware", expense.Element("undo")!.Element("nuyentype")!.Value);
        Assert.AreEqual(s_InstanceId.ToString("D"), expense.Element("undo")!.Element("objectid")!.Value);
        Assert.AreNotEqual(ware.Element("sourceid")!.Value, ware.Element("guid")!.Value);
        Assert.AreEqual("30", root.Element("cyberwares")!.Elements("cyberware").Single(node =>
            string.Equals(node.Element("sourceid")?.Value, HoleSourceId, StringComparison.Ordinal)).Element("rating")!.Value);

        CharacterCyberwarePurchasePreparation restarted = authority.Prepare(committed.CharacterXml, 43);
        Assert.IsTrue(restarted.Exact, string.Join("; ", restarted.Blockers));
        Assert.AreEqual(committed.NewCharacterDigest, restarted.CharacterDigest);
        Assert.AreEqual(preparation.CatalogDigest, restarted.CatalogDigest);

        CharacterCyberwarePurchaseCommitResult undone = authority.Undo(
            committed.CharacterXml,
            43,
            new CharacterCyberwarePurchaseUndoCommand(undoReceipt));
        Assert.IsTrue(undone.Committed, undone.BlockReason);
        XDocument afterUndo = XDocument.Parse(undone.CharacterXml, LoadOptions.None);
        Assert.AreEqual("10000.00", afterUndo.Root!.Element("nuyen")!.Value);
        Assert.IsFalse(afterUndo.Root.Element("cyberwares")!.Elements("cyberware").Any(node =>
            string.Equals(node.Element("guid")?.Value, s_InstanceId.ToString("D"), StringComparison.Ordinal)));
        Assert.IsFalse(afterUndo.Root.Element("expenses")!.Elements("expense").Any());
        Assert.AreEqual("30", afterUndo.Root.Element("cyberwares")!.Elements("cyberware").Single().Element("rating")!.Value,
            "Chummer5 undo deletes with increase-Essence-Hole=false, so the consumed Hole remains consumed.");
    }

    [TestMethod]
    public void Every_stale_or_colliding_commit_is_before_or_after_atomic()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(before, 7);
        CharacterCyberwarePurchaseSelection selection = SimrigSelection();
        CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selection);
        CharacterCyberwarePurchaseCommand valid = Command(preparation, quote, selection);

        CharacterCyberwarePurchaseCommand[] hostile =
        [
            valid with { ExpectedContentRevision = 8 },
            valid with { ExpectedCharacterDigest = new string('0', 64) },
            valid with { ExpectedCatalogDigest = new string('1', 64) },
            valid with { ExpectedQuoteDigest = new string('2', 64) },
            valid with { NewInstanceId = new CharacterCyberwareInstanceId(Guid.Parse(SimrigId)) },
            valid with { NewInstanceId = new CharacterCyberwareInstanceId(Guid.Parse(StandardGradeId)) },
            valid with { NewExpenseId = s_InstanceId },
            valid with { ExpenseDate = valid.ExpenseDate.ToOffset(TimeSpan.FromHours(1)) }
        ];
        foreach (CharacterCyberwarePurchaseCommand command in hostile)
        {
            CharacterCyberwarePurchaseCommitResult result = authority.Commit(before, 7, command);
            Assert.IsFalse(result.Committed);
            Assert.AreEqual(before, result.CharacterXml);
            Assert.AreEqual(result.PreviousCharacterDigest, result.NewCharacterDigest);
            Assert.AreEqual(7L, result.NewContentRevision);
        }

        string ambiguous = before.Replace("<expenses />", "<expenses /><expenses />", StringComparison.Ordinal);
        CharacterCyberwarePurchasePreparation ambiguousPreparation = authority.Prepare(ambiguous, 7);
        CharacterCyberwarePurchaseQuote ambiguousQuote = authority.Quote(ambiguousPreparation, selection);
        CharacterCyberwarePurchaseCommitResult ambiguousResult = authority.Commit(
            ambiguous,
            7,
            Command(ambiguousPreparation, ambiguousQuote, selection));
        Assert.IsFalse(ambiguousResult.Committed);
        Assert.AreEqual(ambiguous, ambiguousResult.CharacterXml);
    }

    [TestMethod]
    public void Career_profile_and_unsupported_source_gates_never_fall_back_to_quick_add()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        string pending = CharacterXml().Replace("<created>True</created>", "<created>False</created>", StringComparison.Ordinal);
        CharacterCyberwarePurchasePreparation pendingPreparation = authority.Prepare(pending, 1);
        Assert.IsFalse(pendingPreparation.Exact);
        CollectionAssert.Contains(pendingPreparation.Blockers.ToArray(), CharacterCyberwarePurchaseBlockers.NotCareer);

        string improved = CharacterXml().Replace(
            "<improvements />",
            "<improvements><improvement><improvementttype>CyberwareEssCost</improvementttype></improvement></improvements>",
            StringComparison.Ordinal);
        CharacterCyberwarePurchasePreparation improvedPreparation = authority.Prepare(improved, 1);
        Assert.IsFalse(improvedPreparation.Exact);
        CollectionAssert.Contains(improvedPreparation.Blockers.ToArray(), CharacterCyberwarePurchaseBlockers.ImprovementsUnsupported);

        CharacterCyberwarePurchasePreparation exact = authority.Prepare(CharacterXml(), 1);
        var unsupported = new CharacterCyberwarePurchaseSelection(
            new CharacterCyberwareSourceId(Guid.Parse(SkilljackId)),
            new CharacterCyberwareGradeId(Guid.Parse(StandardGradeId)),
            Rating: 1,
            EssenceDiscountPercent: 0,
            BlackMarketDiscount: false,
            MarkupPercent: 0m,
            FreeCost: false);
        CharacterCyberwarePurchaseQuote quote = authority.Quote(exact, unsupported);
        Assert.IsFalse(quote.Exact);
        Assert.AreEqual(0m, quote.NuyenDelta);

        CharacterCyberwarePurchasePreparation overlaid = CreateAuthority(withEnabledOverlay: true)
            .Prepare(CharacterXml(), 1);
        Assert.IsFalse(overlaid.Exact);
        CollectionAssert.Contains(overlaid.Blockers.ToArray(), CharacterCyberwarePurchaseBlockers.OverlaysUnsupported);

        CharacterCyberwarePurchasePreparation exCon = authority.Prepare(
            CharacterXml().Replace("<excon>False</excon>", "<excon>True</excon>", StringComparison.Ordinal),
            1);
        Assert.IsFalse(exCon.Entries.Any(entry => entry.SourceId.Value == Guid.Parse(SimrigId)));
        Assert.IsTrue(exCon.Exclusions.Any(exclusion =>
            exclusion.SourceId.Value == Guid.Parse(SimrigId)
            && exclusion.Reason.Contains("Ex-Con", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Quote_rejects_unbound_selection_values_and_digests_are_deterministic()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        CharacterCyberwarePurchasePreparation first = authority.Prepare(CharacterXml(), 11);
        CharacterCyberwarePurchasePreparation second = authority.Prepare(CharacterXml(), 11);
        Assert.AreEqual(first.CharacterDigest, second.CharacterDigest);
        Assert.AreEqual(first.CatalogDigest, second.CatalogDigest);
        Assert.AreEqual(
            authority.Quote(first, SimrigSelection()).QuoteDigest,
            authority.Quote(second, SimrigSelection()).QuoteDigest);

        CharacterCyberwarePurchaseSelection[] hostile =
        [
            SimrigSelection() with { Rating = 1 },
            SimrigSelection() with { MarkupPercent = -99.01m },
            SimrigSelection() with { MarkupPercent = 1000.01m },
            SimrigSelection() with { MarkupPercent = 0.001m },
            SimrigSelection() with { EssenceDiscountPercent = 1 },
            SimrigSelection() with { SourceId = new CharacterCyberwareSourceId(Guid.Empty) },
            SimrigSelection() with { GradeId = new CharacterCyberwareGradeId(Guid.Empty) }
        ];
        foreach (CharacterCyberwarePurchaseSelection selection in hostile)
            Assert.IsFalse(authority.Quote(first, selection).Exact);
    }

    [TestMethod]
    public void Essence_hole_fractional_centi_rounding_matches_pinned_standard_round_for_both_profiles()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(CharacterXml(), 9);

        CharacterCyberwarePurchaseQuote exactCenti = FractionalQuote(authority, preparation, "0.0100", true, 2);
        Assert.AreEqual(0.0100m, exactCenti.InstalledEssence);
        Assert.AreEqual(49, exactCenti.NewEssenceHoleRating);

        CharacterCyberwarePurchaseQuote fractionalCenti = FractionalQuote(authority, preparation, "0.0101", true, 2);
        Assert.AreEqual(0.0101m, fractionalCenti.InstalledEssence);
        Assert.AreEqual(48, fractionalCenti.NewEssenceHoleRating,
            "Pinned StandardRound ceilings every positive fractional centi-Essence value.");

        CharacterCyberwarePurchaseQuote internallyRoundedDown = FractionalQuote(
            authority,
            preparation,
            "0.0101",
            doNotRoundInternally: false,
            essenceDecimals: 2);
        Assert.AreEqual(0.01m, internallyRoundedDown.InstalledEssence);
        Assert.AreEqual(49, internallyRoundedDown.NewEssenceHoleRating);

        CharacterCyberwarePurchaseQuote internallyRoundedUp = FractionalQuote(
            authority,
            preparation,
            "0.015",
            doNotRoundInternally: false,
            essenceDecimals: 2);
        Assert.AreEqual(0.02m, internallyRoundedUp.InstalledEssence);
        Assert.AreEqual(48, internallyRoundedUp.NewEssenceHoleRating);

        Assert.AreEqual(2, CharacterCyberwarePurchaseRules.StandardRound(1.0001m));
        Assert.AreEqual(-2, CharacterCyberwarePurchaseRules.StandardRound(-1.0001m));
        Assert.AreEqual(1, CharacterCyberwarePurchaseRules.StandardRound(1m));
    }

    [TestMethod]
    public void Purchase_at_or_beyond_remaining_hole_removes_it_and_receipt_undo_stays_exact()
    {
        foreach (int initialHole in new[] { 20, 19 })
        {
            FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
            string before = CharacterXml().Replace(
                "<rating>50</rating>",
                $"<rating>{initialHole}</rating>",
                StringComparison.Ordinal);
            CharacterCyberwarePurchasePreparation preparation = authority.Prepare(before, 30);
            CharacterCyberwarePurchaseSelection selection = SimrigSelection();
            CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selection);
            Assert.IsTrue(quote.Exact, quote.BlockReason);
            Assert.AreEqual(0, quote.NewEssenceHoleRating);

            CharacterCyberwarePurchaseCommitResult committed = authority.Commit(
                before,
                30,
                Command(preparation, quote, selection));
            Assert.IsTrue(committed.Committed, committed.BlockReason);
            XDocument saved = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
            Assert.IsFalse(saved.Root!.Element("cyberwares")!.Elements("cyberware").Any(node =>
                string.Equals(node.Element("sourceid")?.Value, HoleSourceId, StringComparison.Ordinal)));

            CharacterCyberwarePurchaseCommitResult undone = authority.Undo(
                committed.CharacterXml,
                31,
                new CharacterCyberwarePurchaseUndoCommand(committed.UndoReceipt));
            Assert.IsTrue(undone.Committed, undone.BlockReason);
            XDocument afterUndo = XDocument.Parse(undone.CharacterXml, LoadOptions.None);
            Assert.IsFalse(afterUndo.Root!.Element("cyberwares")!.Elements("cyberware").Any(node =>
                string.Equals(node.Element("sourceid")?.Value, HoleSourceId, StringComparison.Ordinal)),
                "Pinned Chummer5 undo does not recreate a consumed Essence Hole.");
        }
    }

    [TestMethod]
    public void Undo_requires_fresh_career_source_and_exact_commit_receipt_under_hostile_xml()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(before, 20);
        CharacterCyberwarePurchaseSelection selection = SimrigSelection(blackMarket: true, markup: 10m);
        CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selection);
        CharacterCyberwarePurchaseCommitResult committed = authority.Commit(
            before,
            20,
            Command(preparation, quote, selection));
        Assert.IsTrue(committed.Committed, committed.BlockReason);
        CharacterCyberwarePurchaseUndoReceipt receipt = committed.UndoReceipt!;

        CharacterCyberwarePurchaseCommitResult missingReceipt = authority.Undo(
            committed.CharacterXml,
            21,
            new CharacterCyberwarePurchaseUndoCommand(null));
        Assert.IsFalse(missingReceipt.Committed);
        Assert.AreEqual(committed.CharacterXml, missingReceipt.CharacterXml);

        AssertUndoBlocked(
            authority,
            committed.CharacterXml.Replace("<created>True</created>", "<created>False</created>", StringComparison.Ordinal),
            21,
            receipt);
        AssertUndoBlocked(
            authority,
            committed.CharacterXml.Replace("<character>", "<runner>", StringComparison.Ordinal)
                .Replace("</character>", "</runner>", StringComparison.Ordinal),
            21,
            receipt);
        AssertUndoBlocked(authority, "<character>", 21, receipt);
        AssertUndoBlocked(authority, string.Empty, 21, receipt);

        CharacterCyberwarePurchaseUndoReceipt staleCatalog = SealReceipt(receipt with
        {
            CatalogDigest = new string('0', 64)
        });
        AssertUndoBlocked(authority, committed.CharacterXml, 21, staleCatalog);

        CharacterCyberwarePurchaseUndoReceipt alteredSource = SealReceipt(receipt with
        {
            SourceId = new CharacterCyberwareSourceId(Guid.Parse(SkilljackId)),
            Selection = receipt.Selection with
            {
                SourceId = new CharacterCyberwareSourceId(Guid.Parse(SkilljackId))
            }
        });
        AssertUndoBlocked(authority, committed.CharacterXml, 21, alteredSource);

        CharacterCyberwarePurchaseUndoReceipt alteredPreviousAuthority = SealReceipt(receipt with
        {
            PreviousAvailableNuyen = receipt.PreviousAvailableNuyen + 1m,
            PreviousEssenceHoleRating = receipt.PreviousEssenceHoleRating + 1
        });
        AssertUndoBlocked(authority, committed.CharacterXml, 21, alteredPreviousAuthority);

        XDocument duplicateWareDocument = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement duplicateWare = duplicateWareDocument.Root!.Element("cyberwares")!.Elements("cyberware")
            .Single(node => node.Element("guid")?.Value == s_InstanceId.ToString("D"));
        duplicateWare.AddAfterSelf(new XElement(duplicateWare));
        string duplicateWareXml = duplicateWareDocument.ToString(SaveOptions.DisableFormatting);
        AssertUndoBlocked(
            authority,
            duplicateWareXml,
            21,
            SealReceipt(receipt with
            {
                CharacterDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(duplicateWareXml)
            }));

        XDocument duplicateExpenseDocument = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement duplicateExpense = duplicateExpenseDocument.Root!.Element("expenses")!.Elements("expense").Single();
        duplicateExpense.AddAfterSelf(new XElement(duplicateExpense));
        string duplicateExpenseXml = duplicateExpenseDocument.ToString(SaveOptions.DisableFormatting);
        AssertUndoBlocked(
            authority,
            duplicateExpenseXml,
            21,
            SealReceipt(receipt with
            {
                CharacterDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(duplicateExpenseXml)
            }));

        XDocument alteredWareDocument = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement alteredWare = alteredWareDocument.Root!.Element("cyberwares")!.Elements("cyberware")
            .Single(node => node.Element("guid")?.Value == s_InstanceId.ToString("D"));
        alteredWare.Element("notes")!.Value = "altered";
        string alteredWareXml = alteredWareDocument.ToString(SaveOptions.DisableFormatting);
        AssertUndoBlocked(
            authority,
            alteredWareXml,
            21,
            SealReceipt(receipt with
            {
                CharacterDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(alteredWareXml),
                CyberwareXmlDigest = ElementDigest(alteredWare)
            }));

        XDocument alteredExpenseDocument = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement alteredExpense = alteredExpenseDocument.Root!.Element("expenses")!.Element("expense")!;
        alteredExpense.Element("refund")!.Value = "True";
        string alteredExpenseXml = alteredExpenseDocument.ToString(SaveOptions.DisableFormatting);
        AssertUndoBlocked(
            authority,
            alteredExpenseXml,
            21,
            SealReceipt(receipt with
            {
                CharacterDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(alteredExpenseXml),
                ExpenseXmlDigest = ElementDigest(alteredExpense)
            }));

        CharacterCyberwarePurchaseCommitResult undone = authority.Undo(
            committed.CharacterXml,
            21,
            new CharacterCyberwarePurchaseUndoCommand(receipt));
        Assert.IsTrue(undone.Committed, undone.BlockReason);
        CharacterCyberwarePurchaseUndoReceipt reentry = SealReceipt(receipt with
        {
            ContentRevision = undone.NewContentRevision,
            CharacterDigest = undone.NewCharacterDigest
        });
        AssertUndoBlocked(authority, undone.CharacterXml, undone.NewContentRevision, reentry);
    }

    [TestMethod]
    public void Changed_catalog_bytes_fail_closed_before_any_row_is_admitted()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"chummer-cyberware-pin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "cyberware.xml"), "<chummer />");
            var overlays = new OverlayCatalog(temporary, enabled: false);
            var authority = new FileSystemCharacterCyberwarePurchaseAuthority(
                overlays,
                new FileSystemCharacterSourceDataResolver(overlays));

            CharacterCyberwarePurchasePreparation preparation = authority.Prepare(CharacterXml(), 3);

            Assert.IsFalse(preparation.Exact);
            CollectionAssert.Contains(
                preparation.Blockers.ToArray(),
                CharacterCyberwarePurchaseBlockers.PinnedCatalogMismatch);
            Assert.AreEqual(0, preparation.Entries.Count);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [TestMethod]
    public void Prepare_hashes_and_parses_one_immutable_catalog_snapshot()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"chummer-cyberware-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string sourcePath = Path.Combine(DataPath(), "cyberware.xml");
            string temporaryPath = Path.Combine(temporary, "cyberware.xml");
            File.Copy(sourcePath, temporaryPath);
            File.Copy(Path.Combine(DataPath(), "settings.xml"), Path.Combine(temporary, "settings.xml"));
            int reads = 0;
            byte[] ReadThenSwap(string path)
            {
                Assert.AreEqual(temporaryPath, path);
                reads++;
                byte[] snapshot = File.ReadAllBytes(path);
                File.WriteAllText(path, "<chummer />");
                return snapshot;
            }

            var overlays = new OverlayCatalog(temporary, enabled: false);
            var sourceOverlays = new OverlayCatalog(DataPath(), enabled: false);
            var authority = new FileSystemCharacterCyberwarePurchaseAuthority(
                overlays,
                new FileSystemCharacterSourceDataResolver(sourceOverlays),
                ReadThenSwap);

            CharacterCyberwarePurchasePreparation preparation = authority.Prepare(CharacterXml(), 3);

            Assert.AreEqual(1, reads);
            Assert.IsTrue(preparation.Exact, string.Join("; ", preparation.Blockers));
            Assert.AreEqual(CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256,
                preparation.CyberwareXmlDigest);
            Assert.IsTrue(preparation.Entries.Any(entry => entry.SourceId.Value == Guid.Parse(SimrigId)));
            string swappedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporaryPath)))
                .ToLowerInvariant();
            Assert.AreNotEqual(preparation.CyberwareXmlDigest, swappedDigest);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [TestMethod]
    public void Null_commit_selection_is_blocked_without_mutation()
    {
        FileSystemCharacterCyberwarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(before, 42);
        CharacterCyberwarePurchaseSelection selection = SimrigSelection();
        CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selection);
        CharacterCyberwarePurchaseCommand command = Command(preparation, quote, selection) with
        {
            Selection = null!
        };

        CharacterCyberwarePurchaseCommitResult result = authority.Commit(before, 42, command);

        Assert.IsFalse(result.Committed);
        Assert.AreEqual(CharacterCyberwarePurchaseBlockers.IdentityInvalid, result.BlockReason);
        Assert.AreEqual(before, result.CharacterXml);
        Assert.AreEqual(42L, result.NewContentRevision);
        Assert.AreEqual(result.PreviousCharacterDigest, result.NewCharacterDigest);
    }

    private static FileSystemCharacterCyberwarePurchaseAuthority CreateAuthority(bool withEnabledOverlay = false)
    {
        var overlays = new OverlayCatalog(DataPath(), withEnabledOverlay);
        return new FileSystemCharacterCyberwarePurchaseAuthority(
            overlays,
            new FileSystemCharacterSourceDataResolver(overlays));
    }

    private static CharacterCyberwarePurchaseSelection SimrigSelection(
        bool blackMarket = false,
        decimal markup = 0m)
        => new(
            new CharacterCyberwareSourceId(Guid.Parse(SimrigId)),
            new CharacterCyberwareGradeId(Guid.Parse(StandardGradeId)),
            Rating: 0,
            EssenceDiscountPercent: 0,
            blackMarket,
            markup,
            FreeCost: false);

    private static CharacterCyberwarePurchaseQuote FractionalQuote(
        FileSystemCharacterCyberwarePurchaseAuthority authority,
        CharacterCyberwarePurchasePreparation preparation,
        string essence,
        bool doNotRoundInternally,
        int essenceDecimals)
    {
        CharacterCyberwarePurchaseCatalogEntry source = preparation.Entries.Single(entry =>
            entry.SourceId.Value == Guid.Parse(SimrigId));
        CharacterCyberwarePurchasePreparation projected = preparation with
        {
            EssenceHoleRating = 50,
            Settings = preparation.Settings with
            {
                DoNotRoundEssenceInternally = doNotRoundInternally,
                EssenceDecimals = essenceDecimals
            },
            Entries = preparation.Entries.Select(entry => entry.SourceId == source.SourceId
                    ? entry with { EssenceExpression = essence }
                    : entry)
                .ToArray()
        };
        CharacterCyberwarePurchaseQuote quote = authority.Quote(projected, SimrigSelection());
        Assert.IsTrue(quote.Exact, quote.BlockReason);
        return quote;
    }

    private static CharacterCyberwarePurchaseUndoReceipt SealReceipt(
        CharacterCyberwarePurchaseUndoReceipt receipt)
    {
        CharacterCyberwarePurchaseUndoReceipt unsigned = receipt with { ReceiptDigest = string.Empty };
        return unsigned with
        {
            ReceiptDigest = CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(unsigned)
        };
    }

    private static string ElementDigest(XElement element)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                element.ToString(SaveOptions.DisableFormatting))))
            .ToLowerInvariant();

    private static void AssertUndoBlocked(
        FileSystemCharacterCyberwarePurchaseAuthority authority,
        string characterXml,
        long revision,
        CharacterCyberwarePurchaseUndoReceipt receipt)
    {
        CharacterCyberwarePurchaseCommitResult result = authority.Undo(
            characterXml,
            revision,
            new CharacterCyberwarePurchaseUndoCommand(receipt));
        Assert.IsFalse(result.Committed, result.BlockReason);
        Assert.AreEqual(characterXml, result.CharacterXml);
        Assert.AreEqual(revision, result.NewContentRevision);
        Assert.AreEqual(result.PreviousCharacterDigest, result.NewCharacterDigest);
    }

    private static CharacterCyberwarePurchaseCommand Command(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseQuote quote,
        CharacterCyberwarePurchaseSelection selection)
        => new(
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            quote.QuoteDigest,
            selection,
            new CharacterCyberwareInstanceId(s_InstanceId),
            s_ExpenseId,
            new DateTimeOffset(2081, 5, 6, 7, 8, 9, TimeSpan.Zero));

    private static void AssertSavedSimrigExactly(XElement ware)
    {
        string[] exactFields =
        [
            "guid", "sourceid", "name", "category", "limbslot", "limbslotcount",
            "inheritattributes", "ess", "capacity", "avail", "cost", "weight", "source", "page",
            "parentid", "hasmodularmount", "plugsintomodularmount", "blocksmounts", "forced", "rating",
            "minagility", "minstrength", "minrating", "maxrating", "ratinglabel", "subsystems", "wirelesson",
            "grade", "location", "extra", "suite", "stolen", "essdiscount", "extraessadditivemultiplier",
            "extraessmultiplicativemultiplier", "forcegrade", "matrixcmfilled", "matrixcmbonus",
            "prototypetranshuman", "bonus", "pairbonus", "wirelessbonus", "wirelesspairbonus",
            "improvementsource", "pairinclude", "wirelesspairinclude", "notes", "notesColor", "discountedcost",
            "addtoparentess", "addtoparentcapacity", "isgeneware", "active", "homenode", "devicerating",
            "programlimit", "overclocked", "canformpersona", "attack", "sleaze", "dataprocessing", "firewall",
            "attributearray", "modattack", "modsleaze", "moddataprocessing", "modfirewall", "modattributearray",
            "canswapattributes", "sortorder"
        ];
        CollectionAssert.AreEqual(exactFields, ware.Elements().Select(node => node.Name.LocalName).ToArray());
        Assert.AreEqual(s_InstanceId.ToString("D"), ware.Element("guid")!.Value);
        Assert.AreEqual(SimrigId, ware.Element("sourceid")!.Value);
        Assert.AreEqual("Simrig", ware.Element("name")!.Value);
        Assert.AreEqual("Headware", ware.Element("category")!.Value);
        Assert.AreEqual("0.2", ware.Element("ess")!.Value);
        Assert.AreEqual("0", ware.Element("capacity")!.Value);
        Assert.AreEqual("12R", ware.Element("avail")!.Value);
        Assert.AreEqual("4000", ware.Element("cost")!.Value);
        Assert.AreEqual("Standard", ware.Element("grade")!.Value);
        Assert.AreEqual("Cyberware", ware.Element("improvementsource")!.Value);
        Assert.AreEqual("Chocolate", ware.Element("notesColor")!.Value);
        Assert.AreEqual("True", ware.Element("discountedcost")!.Value);
        Assert.AreEqual("None", ware.Element("overclocked")!.Value);
        Assert.AreEqual("Simrig", ware.Element("pairinclude")!.Element("name")!.Value);
        Assert.AreEqual("Simrig", ware.Element("wirelesspairinclude")!.Element("name")!.Value);
    }

    private static string CharacterXml()
        => $"""
           <character>
             <settings>{ProfileId}</settings>
             <created>True</created>
             <excon>False</excon>
             <nuyen>10000.00</nuyen>
             <customdatadirectorynames />
             <improvements />
             <cyberwares>
               <cyberware>
                 <guid>33333333-3333-4333-8333-333333333333</guid>
                 <sourceid>{HoleSourceId}</sourceid>
                 <name>Essence Hole</name>
                 <rating>50</rating>
               </cyberware>
             </cyberwares>
             <expenses />
           </character>
           """;

    private static string DataPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data");
            if (File.Exists(Path.Combine(candidate, "cyberware.xml")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the carried Chummer/data corpus.");
    }

    private sealed class OverlayCatalog : IContentOverlayCatalogService
    {
        private readonly ContentOverlayCatalog _catalog;

        public OverlayCatalog(string dataPath, bool enabled)
        {
            ContentOverlayPack[] overlays = enabled
                ? [new ContentOverlayPack("hostile", "Hostile", dataPath, dataPath, dataPath, 1, true,
                    ContentOverlayModes.MergeCatalog, "test")]
                : [];
            _catalog = new ContentOverlayCatalog(dataPath, dataPath, overlays);
        }

        public ContentOverlayCatalog GetCatalog() => _catalog;
        public IReadOnlyList<string> GetDataDirectories() => [_catalog.BaseDataPath];
        public IReadOnlyList<string> GetLanguageDirectories() => [_catalog.BaseLanguagePath];
        public string ResolveDataFile(string fileName) => Path.Combine(_catalog.BaseDataPath, fileName);
    }
}
