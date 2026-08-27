using System.Security.Cryptography;
using System.Xml.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class FileSystemCharacterBiowarePurchaseAuthorityTests
{
    private const string ProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string CatsEyesId = "f038260b-f2de-4a9a-9507-5602d0e64a22";
    private const string AdrenalinePumpId = "ae0bb365-e40c-4aa2-9c30-0be902d992ac";
    private const string PainEditorId = "add913d1-6174-41d6-8021-b8bf35345e7a";
    private const string StandardGradeId = "f0a67dc0-6b0a-43fa-b389-a110ba1dd59d";
    private const string HoleSourceId = "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
    private static readonly Guid s_ConfigurationId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid s_InstanceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid s_ExpenseId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void Effective_catalog_binds_raw_overlay_custom_profile_and_typed_availability_authority()
    {
        FileSystemCharacterBiowarePurchaseAuthority authority = CreateAuthority();
        CharacterBiowarePurchasePreparation preparation = authority.Prepare(CharacterXml(), 42);

        Assert.IsTrue(preparation.Exact, string.Join("; ", preparation.Blockers));
        Assert.AreEqual(64, preparation.CatalogDigest.Length);
        Assert.AreEqual(
            $"sha256:{CharacterBiowarePurchaseLegacyAuthority.BiowareXmlSha256}",
            preparation.SourceBinding.RawBiowareXmlDigest);
        StringAssert.StartsWith(preparation.SourceBinding.EffectiveBiowareInputsDigest, "sha256:");
        StringAssert.StartsWith(preparation.SourceBinding.SelectedBiowareCustomDataInputsDigest, "sha256:");
        StringAssert.StartsWith(preparation.SourceBinding.EffectiveSettingsInputsDigest, "sha256:");

        CharacterBiowarePurchaseCatalogEntry catsEyes = preparation.Entries.Single(entry =>
            entry.SourceId.Value == Guid.Parse(CatsEyesId));
        Assert.AreEqual("Cat's Eyes", catsEyes.Name);
        Assert.AreEqual(4, catsEyes.BaseAvailability);
        Assert.AreEqual(CharacterBiowareLegality.Legal, catsEyes.Legality);
        Assert.IsTrue(catsEyes.BlackMarketEligible);
        Assert.IsFalse(catsEyes.IsGeneware);
        CharacterBiowarePurchaseGrade standard = catsEyes.Grades.Single(grade =>
            grade.Id.Value == Guid.Parse(StandardGradeId));
        Assert.AreEqual(0, standard.AvailabilityModifier);
        Assert.AreNotEqual(catsEyes.SourceId.Value, standard.Id.Value);
        Assert.IsTrue(preparation.Exclusions.Any(exclusion =>
            exclusion.SourceId.Value == Guid.Parse(AdrenalinePumpId)
            && exclusion.Reason.Contains("rating", StringComparison.OrdinalIgnoreCase)));

        string actualRaw = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(DataPath(), "bioware.xml"))))
            .ToLowerInvariant();
        Assert.AreEqual(CharacterBiowarePurchaseLegacyAuthority.BiowareXmlSha256, actualRaw);
        CollectionAssert.Contains(
            CharacterBiowarePurchaseLegacyAuthority.CanonicalInputs.ToArray(),
            $"Chummer/data/bioware.xml:{CharacterBiowarePurchaseLegacyAuthority.BiowareXmlSha256}");
    }

    [TestMethod]
    public void Quote_commit_restart_and_undo_preserve_typed_quote_cost_availability_essence_and_identity()
    {
        FileSystemCharacterBiowarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterBiowarePurchasePreparation preparation = authority.Prepare(before, 42);
        CharacterBiowarePurchaseSelection selection = CatsEyesSelection(blackMarket: true, markup: 10m);
        CharacterBiowarePurchaseQuote quote = authority.Quote(preparation, selection);

        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(s_ConfigurationId, quote.ConfigurationId.Value);
        Assert.AreEqual(64, quote.QuoteId.Value.Length);
        Assert.AreEqual(4_000m, quote.BaseCost);
        Assert.AreEqual(3_960m, quote.ChargedCost);
        Assert.AreEqual(-3_960m, quote.NuyenDelta);
        Assert.AreEqual(0.1m, quote.InstalledEssence);
        Assert.AreEqual(4, quote.BaseAvailability);
        Assert.AreEqual(0, quote.GradeAvailabilityModifier);
        Assert.AreEqual(4, quote.FinalAvailability);
        Assert.AreEqual(CharacterBiowareLegality.Legal, quote.Legality);
        Assert.AreEqual(40, quote.NewEssenceHoleRating);

        CharacterBiowarePurchaseCommitResult committed = authority.Commit(
            before,
            42,
            Command(preparation, quote, selection));
        Assert.IsTrue(committed.Committed, committed.BlockReason);
        Assert.AreEqual(43L, committed.NewContentRevision);
        Assert.AreEqual(-3_960m, committed.NuyenDelta);
        Assert.AreEqual(-0.1m, committed.EssenceHoleDelta);
        Assert.AreEqual(quote.QuoteId, committed.QuoteId);
        Assert.IsNotNull(committed.UndoReceipt);

        XDocument saved = XDocument.Parse(committed.CharacterXml, LoadOptions.None);
        XElement root = saved.Root!;
        Assert.AreEqual("6040.00", root.Element("nuyen")!.Value);
        XElement ware = root.Element("cyberwares")!.Elements("cyberware").Single(node =>
            string.Equals(node.Element("guid")?.Value, s_InstanceId.ToString("D"), StringComparison.Ordinal));
        AssertSavedCatsEyesExactly(ware);
        XElement expense = root.Element("expenses")!.Elements("expense").Single();
        Assert.AreEqual("Purchased Bioware Cat's Eyes", expense.Element("reason")!.Value);
        Assert.AreEqual("AddCyberware", expense.Element("undo")!.Element("nuyentype")!.Value);
        Assert.AreEqual("40", root.Element("cyberwares")!.Elements("cyberware").Single(node =>
            string.Equals(node.Element("sourceid")?.Value, HoleSourceId, StringComparison.Ordinal))
            .Element("rating")!.Value);

        CharacterBiowarePurchasePreparation restarted = authority.Prepare(committed.CharacterXml, 43);
        Assert.IsTrue(restarted.Exact, string.Join("; ", restarted.Blockers));
        Assert.AreEqual(committed.NewCharacterDigest, restarted.CharacterDigest);
        Assert.AreEqual(preparation.CatalogDigest, restarted.CatalogDigest);

        CharacterBiowarePurchaseCommitResult undone = authority.Undo(
            committed.CharacterXml,
            43,
            new CharacterBiowarePurchaseUndoCommand(committed.UndoReceipt));
        Assert.IsTrue(undone.Committed, undone.BlockReason);
        XDocument afterUndo = XDocument.Parse(undone.CharacterXml, LoadOptions.None);
        Assert.AreEqual("10000.00", afterUndo.Root!.Element("nuyen")!.Value);
        Assert.IsFalse(afterUndo.Root.Element("cyberwares")!.Elements("cyberware").Any(node =>
            string.Equals(node.Element("guid")?.Value, s_InstanceId.ToString("D"), StringComparison.Ordinal)));
        Assert.IsFalse(afterUndo.Root.Element("expenses")!.Elements("expense").Any());
        Assert.AreEqual("40", afterUndo.Root.Element("cyberwares")!.Elements("cyberware").Single()
            .Element("rating")!.Value,
            "Pinned Chummer5 AddCyberware undo intentionally leaves the consumed Essence Hole consumed.");
    }

    [TestMethod]
    public void Stale_colliding_and_tampered_commands_and_receipts_fail_before_mutation()
    {
        FileSystemCharacterBiowarePurchaseAuthority authority = CreateAuthority();
        string before = CharacterXml();
        CharacterBiowarePurchasePreparation preparation = authority.Prepare(before, 7);
        CharacterBiowarePurchaseSelection selection = CatsEyesSelection();
        CharacterBiowarePurchaseQuote quote = authority.Quote(preparation, selection);
        CharacterBiowarePurchaseCommand valid = Command(preparation, quote, selection);

        CharacterBiowarePurchaseCommand[] hostile =
        [
            valid with { ExpectedContentRevision = 8 },
            valid with { ExpectedCharacterDigest = new string('0', 64) },
            valid with { ExpectedCatalogDigest = new string('1', 64) },
            valid with { ExpectedQuoteId = new CharacterBiowareQuoteId(new string('2', 64)) },
            valid with { NewInstanceId = new CharacterBiowareInstanceId(Guid.Parse(CatsEyesId)) },
            valid with { NewExpenseId = s_ConfigurationId },
            valid with { ExpenseDate = valid.ExpenseDate.ToOffset(TimeSpan.FromHours(1)) }
        ];
        foreach (CharacterBiowarePurchaseCommand command in hostile)
        {
            CharacterBiowarePurchaseCommitResult result = authority.Commit(before, 7, command);
            Assert.IsFalse(result.Committed);
            Assert.AreEqual(before, result.CharacterXml);
            Assert.AreEqual(7L, result.NewContentRevision);
            Assert.AreEqual(result.PreviousCharacterDigest, result.NewCharacterDigest);
        }

        CharacterBiowarePurchaseCommitResult committed = authority.Commit(before, 7, valid);
        Assert.IsTrue(committed.Committed, committed.BlockReason);
        CharacterBiowarePurchaseUndoReceipt tampered = committed.UndoReceipt! with
        {
            NuyenDelta = committed.UndoReceipt!.NuyenDelta - 1m
        };
        CharacterBiowarePurchaseCommitResult undo = authority.Undo(
            committed.CharacterXml,
            8,
            new CharacterBiowarePurchaseUndoCommand(tampered));
        Assert.IsFalse(undo.Committed);
        Assert.AreEqual(committed.CharacterXml, undo.CharacterXml);
        Assert.AreEqual(8L, undo.NewContentRevision);
    }

    [TestMethod]
    public void Availability_legality_grade_modifier_excon_and_cost_multiplier_are_bound_to_quote()
    {
        FileSystemCharacterBiowarePurchaseAuthority authority = CreateAuthority();
        string wealthy = CharacterXml(nuyen: "200000.00");
        CharacterBiowarePurchasePreparation preparation = authority.Prepare(wealthy, 9);
        CharacterBiowarePurchaseCatalogEntry painEditor = preparation.Entries.Single(entry =>
            entry.SourceId.Value == Guid.Parse(PainEditorId));
        CharacterBiowarePurchaseGrade alpha = painEditor.Grades.Single(grade => grade.Name == "Alphaware");
        var selection = new CharacterBiowarePurchaseSelection(
            new CharacterBiowareConfigurationId(s_ConfigurationId),
            painEditor.SourceId,
            alpha.Id,
            0,
            0,
            false,
            0m,
            false);
        CharacterBiowarePurchasePreparation multiplied = preparation with
        {
            Settings = preparation.Settings with
            {
                MultiplyForbiddenCost = true,
                ForbiddenCostMultiplier = 2m
            }
        };
        CharacterBiowarePurchaseQuote quote = authority.Quote(multiplied, selection);
        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(18, quote.BaseAvailability);
        Assert.AreEqual(2, quote.GradeAvailabilityModifier);
        Assert.AreEqual(20, quote.FinalAvailability);
        Assert.AreEqual(CharacterBiowareLegality.Forbidden, quote.Legality);
        Assert.AreEqual(57_600m, quote.BaseCost);
        Assert.AreEqual(115_200m, quote.ChargedCost);

        CharacterBiowarePurchasePreparation exCon = authority.Prepare(
            wealthy.Replace("<excon>False</excon>", "<excon>True</excon>", StringComparison.Ordinal),
            9);
        Assert.IsTrue(exCon.Exact, string.Join("; ", exCon.Blockers));
        Assert.IsFalse(authority.Quote(exCon, selection).Exact);
    }

    [TestMethod]
    public void Selected_custom_data_row_and_digest_are_effective_and_drift_bound()
    {
        string root = CreateTempDirectory();
        try
        {
            const string directoryName = "Bioware Rules";
            const string customId = "77777777-7777-4777-8777-777777777777";
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            File.Copy(Path.Combine(DataPath(), "bioware.xml"), Path.Combine(data, "bioware.xml"));
            XDocument settings = XDocument.Load(Path.Combine(DataPath(), "settings.xml"), LoadOptions.None);
            XElement profile = settings.Root!.Element("settings")!.Elements("setting").Single(row =>
                string.Equals(row.Element("id")?.Value, ProfileId, StringComparison.OrdinalIgnoreCase));
            profile.Element("customdatadirectorynames")!.Add(
                new XElement("customdatadirectoryname",
                    new XElement("directoryname", directoryName),
                    new XElement("order", "0"),
                    new XElement("enabled", "True")));
            settings.Save(Path.Combine(data, "settings.xml"), SaveOptions.DisableFormatting);
            string customRoot = Path.Combine(root, "customdata", directoryName);
            Directory.CreateDirectory(customRoot);
            string customPath = Path.Combine(customRoot, "custom_bioware.xml");
            WriteCustomBioware(customPath, customId, "1234");

            FileSystemCharacterBiowarePurchaseAuthority authority = CreateAuthority(root);
            string character = CharacterXml(
                customDirectories: $"<directoryname>{directoryName}</directoryname>");
            CharacterBiowarePurchasePreparation first = authority.Prepare(character, 1);
            Assert.IsTrue(first.Exact, string.Join("; ", first.Blockers));
            CharacterBiowarePurchaseCatalogEntry custom = first.Entries.Single(entry =>
                entry.SourceId.Value == Guid.Parse(customId));
            Assert.AreEqual("1234", custom.CostExpression);
            Assert.IsTrue(custom.IsGeneware,
                "Pinned Cyberware.CreateAsync treats an empty isgeneware element as true.");
            Assert.IsTrue(custom.BlackMarketEligible);
            StringAssert.StartsWith(first.SourceBinding.SelectedBiowareCustomDataInputsDigest, "sha256:");

            WriteCustomBioware(customPath, customId, "2345");
            FileSystemCharacterBiowarePurchaseAuthority driftedAuthority = CreateAuthority(root);
            CharacterBiowarePurchasePreparation drifted = driftedAuthority.Prepare(character, 1);
            Assert.IsTrue(drifted.Exact, string.Join("; ", drifted.Blockers));
            Assert.AreEqual("2345", drifted.Entries.Single(entry =>
                entry.SourceId.Value == Guid.Parse(customId)).CostExpression);
            Assert.AreNotEqual(first.SourceBinding.SelectedBiowareCustomDataInputsDigest,
                drifted.SourceBinding.SelectedBiowareCustomDataInputsDigest);
            Assert.AreNotEqual(first.CatalogDigest, drifted.CatalogDigest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileSystemCharacterBiowarePurchaseAuthority CreateAuthority()
    {
        var overlays = new OverlayCatalog(DataPath());
        return new FileSystemCharacterBiowarePurchaseAuthority(
            new FileSystemCharacterSourceDataResolver(overlays));
    }

    private static FileSystemCharacterBiowarePurchaseAuthority CreateAuthority(string root)
    {
        var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
        return new FileSystemCharacterBiowarePurchaseAuthority(
            new FileSystemCharacterSourceDataResolver(overlays));
    }

    private static CharacterBiowarePurchaseSelection CatsEyesSelection(
        bool blackMarket = false,
        decimal markup = 0m)
        => new(
            new CharacterBiowareConfigurationId(s_ConfigurationId),
            new CharacterBiowareSourceId(Guid.Parse(CatsEyesId)),
            new CharacterBiowareGradeId(Guid.Parse(StandardGradeId)),
            0,
            0,
            blackMarket,
            markup,
            false);

    private static CharacterBiowarePurchaseCommand Command(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseQuote quote,
        CharacterBiowarePurchaseSelection selection)
        => new(
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            quote.QuoteId,
            selection,
            new CharacterBiowareInstanceId(s_InstanceId),
            s_ExpenseId,
            new DateTimeOffset(2081, 5, 6, 7, 8, 9, TimeSpan.Zero));

    private static void AssertSavedCatsEyesExactly(XElement ware)
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
        Assert.AreEqual(CatsEyesId, ware.Element("sourceid")!.Value);
        Assert.AreEqual("Cat's Eyes", ware.Element("name")!.Value);
        Assert.AreEqual("Basic", ware.Element("category")!.Value);
        Assert.AreEqual("0.1", ware.Element("ess")!.Value);
        Assert.AreEqual("0", ware.Element("capacity")!.Value);
        Assert.AreEqual("4", ware.Element("avail")!.Value);
        Assert.AreEqual("4000", ware.Element("cost")!.Value);
        Assert.AreEqual("Standard", ware.Element("grade")!.Value);
        Assert.AreEqual("Bioware", ware.Element("improvementsource")!.Value);
        Assert.AreEqual("False", ware.Element("isgeneware")!.Value);
        Assert.AreEqual("True", ware.Element("discountedcost")!.Value);
        Assert.AreEqual("Cat's Eyes", ware.Element("pairinclude")!.Element("name")!.Value);
        Assert.AreEqual("Cat's Eyes", ware.Element("wirelesspairinclude")!.Element("name")!.Value);
    }

    private static string CharacterXml(
        string nuyen = "10000.00",
        string customDirectories = "")
        => $"""
           <character>
             <settings>{ProfileId}</settings>
             <created>True</created>
             <excon>False</excon>
             <nuyen>{nuyen}</nuyen>
             <customdatadirectorynames>{customDirectories}</customdatadirectorynames>
             <improvements />
             <cyberwares>
               <cyberware>
                 <guid>44444444-4444-4444-8444-444444444444</guid>
                 <sourceid>{HoleSourceId}</sourceid>
                 <name>Essence Hole</name>
                 <rating>50</rating>
               </cyberware>
             </cyberwares>
             <expenses />
           </character>
           """;

    private static void WriteCustomBioware(string path, string id, string cost)
        => File.WriteAllText(path,
            $"<chummer><biowares><bioware><id>{id}</id><name>Custom Eyes</name>"
            + "<category>Genetic Restoration</category><ess>0.05</ess><capacity>0</capacity><avail>5R</avail>"
            + $"<cost>{cost}</cost><isgeneware /><source>SR5</source><page>1</page>"
            + "</bioware></biowares></chummer>");

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-bioware-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string DataPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data");
            if (File.Exists(Path.Combine(candidate, "bioware.xml")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the carried Chummer/data corpus.");
    }

    private sealed class OverlayCatalog : IContentOverlayCatalogService
    {
        private readonly ContentOverlayCatalog _catalog;

        public OverlayCatalog(string dataPath)
        {
            _catalog = new ContentOverlayCatalog(dataPath, dataPath, []);
        }

        public ContentOverlayCatalog GetCatalog() => _catalog;
        public IReadOnlyList<string> GetDataDirectories() => [_catalog.BaseDataPath];
        public IReadOnlyList<string> GetLanguageDirectories() => [_catalog.BaseLanguagePath];
        public string ResolveDataFile(string fileName) => Path.Combine(_catalog.BaseDataPath, fileName);
    }
}
