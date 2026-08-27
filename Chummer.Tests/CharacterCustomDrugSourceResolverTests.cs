using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCustomDrugSourceResolverTests
{
    private const string StandardProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string FullHouseProfileId = "67e25032-2a4e-42ca-97fa-69f7f608236c";

    [TestMethod]
    public void Effective_full_house_profile_projects_exact_CF_grades_components_levels_and_source_anchors()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        ICharacterSourceDataContext context = resolver.TryCreateContext(FullHouseCharacterXml())!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCustomDrugCatalog(
            out CharacterCustomDrugCatalogAuthority authority));
        Assert.IsTrue(CharacterCustomDrugRules.IsValidCatalogAuthority(authority),
            string.Join(';', authority.Blockers));
        Assert.AreEqual("sr5", authority.RulesetId);
        Assert.AreEqual(FullHouseProfileId, authority.SettingsProfileId);
        Assert.AreEqual(4, authority.Grades.Count);
        Assert.IsTrue(authority.Components.Count >= 20);
        CharacterCustomDrugGrade pharmaceutical = authority.Grades.Single(item =>
            item.Name == "Pharmaceutical");
        Assert.AreEqual(2m, pharmaceutical.CostMultiplier);
        Assert.AreEqual(-1, pharmaceutical.AddictionThresholdModifier);
        CharacterCustomDrugComponentSource tank = authority.Components.Single(item => item.Name == "Tank");
        Assert.AreEqual(CharacterCustomDrugComponentCategory.Foundation, tank.Category);
        Assert.AreEqual(4, tank.AvailabilityModifier);
        Assert.AreEqual(CharacterCustomDrugLegality.Restricted, tank.Legality);
        Assert.AreEqual(75m, tank.CostPerLevel);
        Assert.AreEqual(6, tank.AddictionRating);
        Assert.AreEqual(2, tank.AddictionThreshold);
        Assert.AreEqual(1, tank.Effects.Count);
        Assert.AreEqual(2m, tank.Effects[0].Attributes.Single(item => item.Attribute == "BOD").Value);
        CharacterCustomDrugComponentSource crush = authority.Components.Single(item => item.Name == "Crush");
        Assert.AreEqual(3, crush.Effects.Count);
        Assert.AreEqual(2, crush.Effects.Single(item => item.Level == 2).CrashDamage);
        CharacterCustomDrugComponentSource speed = authority.Components.Single(item => item.Name == "Speed Enhancer");
        Assert.AreEqual(3, speed.Limit);
        Assert.AreEqual(-3, speed.Effects.Single().Speed);
        Assert.IsTrue(authority.Components.All(item =>
            item.SourceAnchorIds.Single().StartsWith("drugcomponents.xml#drugcomponent:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Disabled_CF_book_exposes_no_custom_drug_authority()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml(StandardProfileId))!;

        Assert.IsNotNull(context);
        Assert.IsFalse(context.TryResolveCustomDrugCatalog(out CharacterCustomDrugCatalogAuthority authority));
        Assert.AreSame(CharacterCustomDrugCatalogAuthority.Unavailable, authority);
    }

    [TestMethod]
    public void Effective_catalog_quote_is_bound_to_profile_character_revision_and_exact_source_rows()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        string characterXml = FullHouseCharacterXml();
        ICharacterSourceDataContext context = resolver.TryCreateContext(characterXml)!;
        Assert.IsTrue(context.TryResolveCustomDrugCatalog(out CharacterCustomDrugCatalogAuthority authority));
        CharacterCustomDrugPreparation preparation = CharacterCustomDrugRules.BindPreparation(
            authority,
            CharacterCustomDrugContext.Career,
            CharacterCustomDrugQuotePurpose.QuantityPurchase,
            contentRevision: 17,
            CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
            availableNuyen: 5_000m);
        CharacterCustomDrugComponentSource tank = authority.Components.Single(item => item.Name == "Tank");
        CharacterCustomDrugComponentSource crush = authority.Components.Single(item => item.Name == "Crush");
        CharacterCustomDrugGrade pharmaceutical = authority.Grades.Single(item => item.Name == "Pharmaceutical");
        var selection = new CharacterCustomDrugSelection(
            "Redline",
            pharmaceutical.Id,
            Quantity: 2m,
            Stolen: false,
            FreeCost: false,
            MarkupPercent: 0m,
            Components:
            [
                new CharacterCustomDrugComponentSelection(tank.Id, 0),
                new CharacterCustomDrugComponentSelection(crush.Id, 1)
            ]);

        CharacterCustomDrugQuote quote = CharacterCustomDrugRules.Quote(preparation, selection);

        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(95m, quote.UnitCost);
        Assert.AreEqual(190m, quote.ChargedCost);
        Assert.AreEqual(5, quote.Availability);
        Assert.AreEqual(CharacterCustomDrugLegality.Restricted, quote.Legality);
        Assert.AreEqual(64, quote.QuoteDigest.Length);
        Assert.AreNotEqual(quote.QuoteDigest, CharacterCustomDrugRules.Quote(
            preparation with { ContentRevision = 18 }, selection).QuoteDigest);
    }

    private static string CharacterXml(string profileId)
        => $"<character><settings>{profileId}</settings></character>";

    private static string FullHouseCharacterXml()
        => $"""
           <character>
             <settings>{FullHouseProfileId}</settings>
             <customdatadirectorynames>
               <directoryname>Chrome Flesh Stealth Errata</directoryname>
               <directoryname>Dark Terrors Stealth Errata</directoryname>
               <directoryname>Forbidden Arcana Stealth Errata</directoryname>
               <directoryname>No Future Stealth Errata</directoryname>
             </customdatadirectorynames>
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
