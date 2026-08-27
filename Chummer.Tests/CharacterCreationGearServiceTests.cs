using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationResourcesServiceTests
{
    [TestMethod]
    public void Gear_basket_is_atomic_reopenable_budgeted_and_keeps_xml_byte_identical()
    {
        WithService((store, resourcesService, id, originalXml, directory) =>
        {
            CharacterCreationResourcesState resources = Load(resourcesService, id);
            CharacterCreationResourcesPreview resourcePreview = resourcesService.Preview(
                new CharacterCreationResourcesPreviewRequest(resources.Binding, "karma:0")).Value!;
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resourcesService.Confirm(
                new CharacterCreationResourcesConfirmRequest(
                    resources.Binding,
                    "karma:0",
                    resourcePreview.PreviewDigest,
                    "gear-resource-budget",
                    ExplicitlyConfirmed: true)).Outcome);

            CharacterCreationGearAuthority gearAuthority = GearAuthority();
            var service = new CharacterCreationGearService(
                store,
                Resolver(PrerequisiteAuthority(), ResourcesAuthority(), gearAuthority));
            CharacterCreationGearState state = service.Load(
                new CharacterCreationGearLoadRequest(id)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(',', state.Blockers));
            Assert.AreEqual(50_000m, state.Budget.TotalStartingNuyen);
            Assert.AreEqual(2, state.Authority.Options.Count);
            Assert.IsFalse(state.Authority.Options.Single(item =>
                item.Name == "Variable Focus").IsSelectable);

            CharacterCreationGearCatalogOption medkit = state.Authority.Options.Single(item =>
                item.Name == "Medkit Supplies");
            CharacterCreationGearSelection[] basket = [new(medkit.OptionId, 20)];
            CharacterCreationGearPreview preview = service.Preview(
                new CharacterCreationGearPreviewRequest(state.Binding, basket)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(',', preview.Blockers));
            Assert.AreEqual(1_000m, preview.BudgetAfter.BasketCost);
            Assert.AreEqual(49_000m, preview.BudgetAfter.RemainingNuyen);
            Assert.AreEqual(10, preview.After.Lines.Single().PackageQuantity);

            var request = new CharacterCreationGearConfirmRequest(
                state.Binding,
                basket,
                preview.PreviewDigest,
                "android-gear-basket-001",
                ExplicitlyConfirmed: true);
            CharacterCreationGearResult<CharacterCreationGearReceipt> applied = service.Confirm(request);
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied, applied.Outcome);
            Assert.IsFalse(applied.Value!.CharacterDocumentChanged);
            Assert.AreEqual(originalXml, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(4L, store.Get(id).Value!.ContentRevision);
            Assert.IsTrue(CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
                id,
                4,
                store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationGearDraft,
                store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationGearReceipts));

            CharacterCreationResourcesState updatedBudget = Load(resourcesService, id);
            Assert.AreEqual(1_000m, updatedBudget.Budget.KnownPurchaseCost);
            Assert.AreEqual(49_000m, updatedBudget.Budget.RemainingNuyen);

            var restarted = new CharacterCreationGearService(
                new FileWorkspaceStore(directory),
                Resolver(PrerequisiteAuthority(), ResourcesAuthority(), gearAuthority));
            CharacterCreationGearState reopened = restarted.Load(
                new CharacterCreationGearLoadRequest(id)).Value!;
            Assert.AreEqual(1_000m, reopened.PendingDraft!.Budget.BasketCost);
            Assert.AreEqual(CharacterCreationGearOutcomes.Available, restarted.LookupReceipt(
                new CharacterCreationGearReceiptLookupRequest(
                    id,
                    "android-gear-basket-001")).Outcome);
            Assert.AreEqual(CharacterCreationGearOutcomes.Replayed, restarted.Confirm(request).Outcome);
            Assert.AreEqual(4L, new FileWorkspaceStore(directory).Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    public void Gear_constraints_stale_binding_and_missing_consent_fail_closed()
    {
        WithService((store, resourcesService, id, _, _) =>
        {
            CharacterCreationResourcesState resources = Load(resourcesService, id);
            CharacterCreationResourcesPreview resourcePreview = resourcesService.Preview(
                new CharacterCreationResourcesPreviewRequest(resources.Binding, "karma:0")).Value!;
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resourcesService.Confirm(
                new CharacterCreationResourcesConfirmRequest(
                    resources.Binding,
                    "karma:0",
                    resourcePreview.PreviewDigest,
                    "gear-resource-constraints",
                    ExplicitlyConfirmed: true)).Outcome);
            var service = new CharacterCreationGearService(
                store,
                Resolver(PrerequisiteAuthority(), ResourcesAuthority(), GearAuthority()));
            CharacterCreationGearState state = service.Load(
                new CharacterCreationGearLoadRequest(id)).Value!;
            CharacterCreationGearCatalogOption disabled = state.Authority.Options.Single(item =>
                item.Name == "Variable Focus");
            CharacterCreationGearResult<CharacterCreationGearPreview> unsupported = service.Preview(
                new CharacterCreationGearPreviewRequest(
                    state.Binding,
                    [new CharacterCreationGearSelection(disabled.OptionId, 1)]));
            Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, unsupported.Outcome);
            CollectionAssert.Contains(unsupported.Blockers.ToList(),
                CharacterCreationGearBlockers.UnsupportedSemantics);

            CharacterCreationGearCatalogOption medkit = state.Authority.Options.Single(item =>
                item.Name == "Medkit Supplies");
            CharacterCreationGearResult<CharacterCreationGearPreview> overspend = service.Preview(
                new CharacterCreationGearPreviewRequest(
                    state.Binding,
                    [new CharacterCreationGearSelection(medkit.OptionId, 1_000_000)]));
            Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, overspend.Outcome);
            CollectionAssert.Contains(overspend.Blockers.ToList(),
                CharacterCreationGearBlockers.InsufficientFunds);

            CharacterCreationGearResult<CharacterCreationGearPreview> stale = service.Preview(
                new CharacterCreationGearPreviewRequest(
                    state.Binding with { SourceDigest = Digest('9') },
                    [new CharacterCreationGearSelection(medkit.OptionId, 10)]));
            Assert.AreEqual(CharacterCreationGearOutcomes.Conflict, stale.Outcome);
            CharacterCreationGearPreview preview = service.Preview(
                new CharacterCreationGearPreviewRequest(
                    state.Binding,
                    [new CharacterCreationGearSelection(medkit.OptionId, 10)])).Value!;
            Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, service.Confirm(
                new CharacterCreationGearConfirmRequest(
                    state.Binding,
                    [new CharacterCreationGearSelection(medkit.OptionId, 10)],
                    preview.PreviewDigest,
                    "gear-no-consent",
                    ExplicitlyConfirmed: false)).Outcome);
            Assert.AreEqual(3L, store.Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    public void Current_Chummer5_corpus_projects_active_source_fixed_gear_and_disables_formulas()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        ICharacterSourceDataContext context = resolver.TryCreateContext(FixtureXml())!;
        Assert.IsTrue(context.TryResolveCreationGearAuthority(
            out CharacterCreationGearAuthority authority));
        Assert.IsTrue(CharacterCreationGearRules.IsValidAuthority(authority),
            string.Join(',', authority.Blockers));
        Assert.IsTrue(authority.Options.Any(option => option.IsSelectable));
        Assert.IsTrue(authority.Options.Any(option => !option.IsSelectable
            && option.Blockers.Contains(CharacterCreationGearBlockers.UnsupportedSemantics)));
        Assert.IsTrue(authority.Options.Where(option => option.IsSelectable)
            .All(option => option.Availability <= authority.MaximumAvailability
                           && option.PricingIsExact
                           && option.AvailabilityIsExact
                           && CharacterCreationGearRules.DigestsEqual(
                               option.SourceNodeDigest,
                               CharacterCreationGearRules.ComputeSourceNodeDigest(
                                   option.SourceNodeXml))));
    }

    [TestMethod]
    public void Core_dependency_injection_registers_creation_gear_authority()
    {
        ServiceCollection services = new();
        services.AddChummerHeadlessCore(AppContext.BaseDirectory, AppContext.BaseDirectory);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<CharacterCreationGearService>(
            provider.GetRequiredService<ICharacterCreationGearService>());
    }

    private static CharacterCreationGearAuthority GearAuthority()
    {
        CharacterCreationGearCatalogOption exact = GearOption(
            Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111"),
            "Medkit Supplies",
            500m,
            10,
            8,
            CharacterCreationGearLegality.Restricted,
            selectable: true,
            []);
        CharacterCreationGearCatalogOption unsupported = GearOption(
            Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222"),
            "Variable Focus",
            0m,
            1,
            0,
            CharacterCreationGearLegality.Legal,
            selectable: false,
            [CharacterCreationGearBlockers.UnsupportedSemantics],
            pricingExact: false,
            availabilityExact: false);
        var candidate = new CharacterCreationGearAuthority(
            CharacterCreationGearSchemas.AuthorityV1,
            "sr5",
            PrerequisiteAuthority().SettingsProfileId,
            12,
            4096,
            1_000_000,
            [exact, unsupported],
            CharacterCreationGearSourceAnchors.All,
            [],
            true,
            Digest('4'),
            Digest('5'),
            Digest('6'),
            Digest('7'),
            string.Empty);
        return candidate with
        {
            AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(candidate)
        };
    }

    private static CharacterCreationGearCatalogOption GearOption(
        Guid id,
        string name,
        decimal cost,
        int costFor,
        int availability,
        string legality,
        bool selectable,
        IReadOnlyList<string> blockers,
        bool pricingExact = true,
        bool availabilityExact = true)
    {
        string sourceNodeXml = $"<gear><id>{id:D}</id><name>{name}</name><category>Biotech</category><rating>0</rating><avail>{availability}</avail><costfor>{costFor}</costfor><cost>{cost}</cost><source>SR5</source><page>450</page></gear>";
        var candidate = new CharacterCreationGearCatalogOption(
            $"gear:{id:D}",
            id,
            name,
            "Biotech",
            cost,
            costFor,
            availability,
            legality,
            "SR5",
            "450",
            selectable,
            pricingExact,
            availabilityExact,
            blockers,
            [$"gear.xml#gear:{id:D}"],
            sourceNodeXml,
            CharacterCreationGearRules.ComputeSourceNodeDigest(sourceNodeXml),
            string.Empty);
        return candidate with
        {
            OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(candidate)
        };
    }
}
