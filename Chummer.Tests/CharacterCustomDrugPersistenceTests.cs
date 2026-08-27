using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Xml;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCustomDrugPersistenceTests
{
    private static readonly CharacterCustomDrugComponentId s_Tank = new(
        Guid.Parse("33ae6b1c-62f6-4824-967d-0e2b37c7d1b9"));
    private static readonly CharacterCustomDrugComponentId s_Crush = new(
        Guid.Parse("9f4c87ba-4a0d-48e8-8c90-08b689da7203"));
    private static readonly CharacterCustomDrugGradeId s_Pharmaceutical = new(
        Guid.Parse("b3366009-4884-44d7-9efa-34e213a75e7e"));
    private static readonly CharacterCustomDrugInstanceId s_DrugInstance = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"));
    private static readonly Guid s_TankInstance = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid s_CrushInstance = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [TestMethod]
    public void Headless_composition_registers_one_shared_custom_drug_authority()
    {
        string root = FindCoreRoot();
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(root, root);
        ServiceDescriptor[] descriptors = services
            .Where(value => value.ServiceType == typeof(ICharacterCustomDrugAuthority))
            .ToArray();

        Assert.AreEqual(1, descriptors.Length);
        Assert.AreEqual(typeof(FileSystemCharacterCustomDrugAuthority), descriptors[0].ImplementationType);
        Assert.AreEqual(ServiceLifetime.Singleton, descriptors[0].Lifetime);
    }

    [TestMethod]
    public void Career_recipe_commit_reopens_recovers_idempotently_and_undoes_without_expense()
    {
        FileSystemCharacterCustomDrugAuthority authority = Authority();
        string characterXml = FullHouseCareerXml();
        CharacterCustomDrugPreparation preparation = authority.Prepare(
            characterXml,
            contentRevision: 10,
            CharacterCustomDrugContext.Career);
        Assert.IsTrue(preparation.Exact, string.Join(';', preparation.Blockers));
        CharacterCustomDrugSelection selection = Recipe();
        CharacterCustomDrugQuote quote = authority.Quote(preparation, selection);
        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(95m, quote.UnitCost);
        Assert.AreEqual(0m, quote.ChargedCost);
        Assert.AreEqual(0m, quote.NuyenDelta);
        CharacterCustomDrugCommitCommand command = Command(preparation, quote, selection);

        CharacterCustomDrugCommitResult committed = authority.Commit(
            characterXml,
            currentContentRevision: 10,
            CharacterCustomDrugContext.Career,
            command);

        Assert.IsTrue(committed.Committed, committed.BlockReason);
        Assert.IsFalse(committed.AlreadyCommitted);
        Assert.AreEqual(11, committed.NewContentRevision);
        Assert.IsNotNull(committed.Receipt);
        XDocument saved = XDocument.Parse(committed.CharacterXml);
        XElement root = saved.Root!;
        Assert.AreEqual("5000", root.Element("nuyen")!.Value);
        Assert.IsNull(root.Element("expenses"));
        XElement drug = root.Element("drugs")!.Elements("drug").Single();
        Assert.AreEqual(s_DrugInstance.Value.ToString("D"), drug.Element("guid")!.Value);
        Assert.AreEqual("Redline", drug.Element("name")!.Value);
        Assert.AreEqual("Custom Drug", drug.Element("category")!.Value);
        Assert.AreEqual("1", drug.Element("quantity")!.Value);
        Assert.AreEqual("Chocolate", drug.Element("notesColor")!.Value);
        Assert.IsNull(drug.Element("cost"));
        Assert.IsNull(drug.Element("rating"));
        Assert.IsNull(drug.Element("threshold"));
        XElement[] components = drug.Element("drugcomponents")!.Elements("drugcomponent").ToArray();
        Assert.AreEqual(2, components.Length);
        Assert.AreEqual(s_TankInstance.ToString("D"), components[0].Element("guid")!.Value);
        Assert.AreEqual(s_CrushInstance.ToString("D"), components[1].Element("guid")!.Value);
        CollectionAssert.AreEqual(
            new[] { "0", "1", "2" },
            components[1].Element("effects")!.Elements("effect")
                .Select(value => value.Element("level")!.Value).ToArray(),
            "All source effect levels must survive save/reopen; Chummer5's missing effect-level save bug is not reproduced.");

        CharacterCustomDrugPreparation reopened = authority.Prepare(
            committed.CharacterXml,
            contentRevision: 11,
            CharacterCustomDrugContext.Career);
        Assert.IsTrue(reopened.Exact, string.Join(';', reopened.Blockers));
        CharacterCustomDrugCommitResult recovered = authority.LookupReceipt(
            committed.CharacterXml,
            currentContentRevision: 11,
            CharacterCustomDrugContext.Career,
            command);
        Assert.IsTrue(recovered.Committed, recovered.BlockReason);
        Assert.IsTrue(recovered.AlreadyCommitted);
        Assert.AreEqual(committed.Receipt!.ReceiptDigest, recovered.Receipt!.ReceiptDigest);
        CharacterCustomDrugCommitResult retried = authority.Commit(
            committed.CharacterXml,
            currentContentRevision: 11,
            CharacterCustomDrugContext.Career,
            command);
        Assert.IsTrue(retried.AlreadyCommitted);

        CharacterCustomDrugCommitResult undone = authority.Undo(
            committed.CharacterXml,
            currentContentRevision: 11,
            CharacterCustomDrugContext.Career,
            new CharacterCustomDrugUndoCommand(recovered.Receipt));
        Assert.IsTrue(undone.Committed, undone.BlockReason);
        Assert.AreEqual(12, undone.NewContentRevision);
        XDocument undoDocument = XDocument.Parse(undone.CharacterXml);
        Assert.IsFalse(undoDocument.Root!.Element("drugs")!.Elements("drug").Any());
        Assert.AreEqual("5000", undoDocument.Root.Element("nuyen")!.Value);
    }

    [TestMethod]
    public void Stale_tampered_collision_and_creation_direct_commit_fail_closed_with_original_bytes()
    {
        FileSystemCharacterCustomDrugAuthority authority = Authority();
        string characterXml = FullHouseCareerXml();
        CharacterCustomDrugPreparation preparation = authority.Prepare(
            characterXml, 10, CharacterCustomDrugContext.Career);
        CharacterCustomDrugSelection selection = Recipe();
        CharacterCustomDrugQuote quote = authority.Quote(preparation, selection);
        CharacterCustomDrugCommitCommand command = Command(preparation, quote, selection);

        CharacterCustomDrugCommitResult stale = authority.Commit(
            characterXml,
            10,
            CharacterCustomDrugContext.Career,
            command with { ExpectedCharacterDigest = new string('a', 64) });
        Assert.IsFalse(stale.Committed);
        Assert.AreEqual(characterXml, stale.CharacterXml);
        Assert.AreEqual(CharacterCustomDrugBlockers.StaleCharacter, stale.BlockReason);
        CharacterCustomDrugCommitResult collision = authority.Commit(
            characterXml,
            10,
            CharacterCustomDrugContext.Career,
            command with { NewComponentInstanceIds = [s_Tank.Value, s_CrushInstance] });
        Assert.IsFalse(collision.Committed);
        Assert.AreEqual(CharacterCustomDrugBlockers.InvalidIdentity, collision.BlockReason);
        Assert.AreEqual(characterXml, collision.CharacterXml);
        CharacterCustomDrugCommitResult creation = authority.Commit(
            characterXml.Replace("<created>True</created>", "<created>False</created>", StringComparison.Ordinal),
            10,
            CharacterCustomDrugContext.Creation,
            command);
        Assert.IsFalse(creation.Committed);
        Assert.AreEqual(CharacterCustomDrugBlockers.CreationMutationRequiresFinalizer, creation.BlockReason);

        CharacterCustomDrugCommitResult committed = authority.Commit(
            characterXml, 10, CharacterCustomDrugContext.Career, command);
        Assert.IsTrue(committed.Committed);
        XDocument tampered = XDocument.Parse(committed.CharacterXml);
        tampered.Root!.Element("drugs")!.Element("drug")!.Element("quantity")!.Value = "9";
        string tamperedXml = tampered.ToString(SaveOptions.DisableFormatting);
        CharacterCustomDrugCommitResult lookup = authority.LookupReceipt(
            tamperedXml, 11, CharacterCustomDrugContext.Career, command);
        Assert.IsFalse(lookup.Committed);
        CharacterCustomDrugCommitResult undo = authority.Undo(
            tamperedXml,
            11,
            CharacterCustomDrugContext.Career,
            new CharacterCustomDrugUndoCommand(committed.Receipt));
        Assert.IsFalse(undo.Committed);
        Assert.AreEqual(CharacterCustomDrugBlockers.StaleReceipt, undo.BlockReason);
        Assert.AreEqual(tamperedXml, undo.CharacterXml);
    }

    private static FileSystemCharacterCustomDrugAuthority Authority()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        return new FileSystemCharacterCustomDrugAuthority(resolver);
    }

    private static CharacterCustomDrugSelection Recipe()
        => new(
            "Redline",
            s_Pharmaceutical,
            Quantity: 1m,
            Stolen: false,
            FreeCost: false,
            MarkupPercent: 0m,
            Components:
            [
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_Crush, 1)
            ]);

    private static CharacterCustomDrugCommitCommand Command(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugQuote quote,
        CharacterCustomDrugSelection selection)
        => new(
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            quote.QuoteDigest,
            "custom-drug:career:10:nonce",
            selection,
            s_DrugInstance,
            [s_TankInstance, s_CrushInstance]);

    private static string FullHouseCareerXml()
        => """
           <character>
             <settings>67e25032-2a4e-42ca-97fa-69f7f608236c</settings>
             <customdatadirectorynames>
               <directoryname>Chrome Flesh Stealth Errata</directoryname>
               <directoryname>Dark Terrors Stealth Errata</directoryname>
               <directoryname>Forbidden Arcana Stealth Errata</directoryname>
               <directoryname>No Future Stealth Errata</directoryname>
             </customdatadirectorynames>
             <created>True</created>
             <nuyen>5000</nuyen>
             <drugs />
           </character>
           """;

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate canonical Chummer/data/settings.xml.");
    }
}
