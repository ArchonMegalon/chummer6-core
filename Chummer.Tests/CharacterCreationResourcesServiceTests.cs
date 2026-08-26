using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationResourcesServiceTests
{
    [TestMethod]
    public void Exact_priority_and_karma_catalog_projects_budget_and_carryover()
    {
        WithService((store, service, id, originalXml, _) =>
        {
            CharacterCreationResourcesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(',', state.Blockers));
            Assert.AreEqual(50_000m, state.Budget.PriorityNuyen);
            Assert.AreEqual(50_000m, state.Budget.TotalStartingNuyen);
            Assert.AreEqual(50_000m, state.Budget.RemainingNuyen);
            Assert.AreEqual(5_000m, state.Budget.CarryoverLimit);
            Assert.AreEqual(45_000m, state.Budget.CarryoverExcess);
            Assert.AreEqual(11, state.Options.Count);

            CharacterCreationResourceAllocationOption option = state.Options.Single(item =>
                item.KarmaInvestment == 10);
            Assert.AreEqual(20_000m, option.NuyenFromKarma);
            Assert.AreEqual(70_000m, option.TotalStartingNuyen);
            Assert.IsTrue(option.IsEnabled);
            AssertDigest(option.OptionDigest);

            CharacterCreationResourcesPreview preview = service.Preview(
                new CharacterCreationResourcesPreviewRequest(state.Binding, option.OptionId)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(',', preview.Blockers));
            Assert.AreEqual(10, preview.BudgetAfter.KarmaInvestment);
            Assert.AreEqual(70_000m, preview.BudgetAfter.TotalStartingNuyen);
            Assert.AreEqual(65_000m, preview.BudgetAfter.CarryoverExcess);
            Assert.AreEqual(50_000m, preview.FinalizationContribution.StartingNuyen);
            Assert.AreEqual(10, preview.FinalizationContribution.NuyenKarma);
            Assert.AreEqual(originalXml, store.Get(id).Value!.Document.Content);
        });
    }

    [TestMethod]
    public void Confirm_is_atomic_idempotent_reopenable_and_keeps_xml_byte_identical()
    {
        WithService((store, service, id, originalXml, directory) =>
        {
            CharacterCreationResourcesState state = Load(service, id);
            CharacterCreationResourceAllocationOption option = state.Options.Single(item =>
                item.KarmaInvestment == 10);
            CharacterCreationResourcesPreview preview = service.Preview(
                new CharacterCreationResourcesPreviewRequest(state.Binding, option.OptionId)).Value!;
            WorkspaceStoredDocument beforeCommit = store.Get(id).Value!;
            var request = new CharacterCreationResourcesConfirmRequest(
                state.Binding,
                option.OptionId,
                preview.PreviewDigest,
                "android-resources-priority-001",
                ExplicitlyConfirmed: true);
            CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> applied =
                service.Confirm(request);

            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, applied.Outcome);
            Assert.IsFalse(applied.Value!.CharacterDocumentChanged);
            Assert.AreEqual(2L, applied.Value.PreviousWorkspaceRevision);
            Assert.AreEqual(3L, applied.Value.WorkspaceRevision);
            Assert.AreEqual(70_000m, applied.Value.TotalStartingNuyen);
            WorkspaceStoredDocument persisted = store.Get(id).Value!;
            Assert.AreEqual(originalXml, persisted.Document.Content);
            Assert.AreEqual(3L, persisted.ContentRevision);
            Assert.AreEqual(3L, persisted.SavedRevision);
            Assert.AreEqual(1L, persisted.Document.AuxiliaryState
                .CharacterCreationResourcesDraft!.DraftRevision);
            Assert.AreEqual(1, persisted.Document.AuxiliaryState
                .CharacterCreationResourcesReceipts!.Count);
            Assert.IsTrue(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                id,
                persisted.ContentRevision,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesReceipts));
            Assert.IsTrue(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidAppendTransition(
                id,
                beforeCommit.ContentRevision,
                beforeCommit.SavedRevision,
                persisted.ContentRevision,
                beforeCommit.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                beforeCommit.Document.AuxiliaryState.CharacterCreationResourcesReceipts,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesReceipts,
                beforeCommit.Document,
                persisted.Document));
            WorkspaceDocument tamperedXml = persisted.Document with
            {
                State = persisted.Document.State with
                {
                    Payload = persisted.Document.Content.Replace(
                        "<sentinel>untouched</sentinel>",
                        "<sentinel>tampered</sentinel>",
                        StringComparison.Ordinal)
                }
            };
            Assert.IsFalse(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidAppendTransition(
                id,
                beforeCommit.ContentRevision,
                beforeCommit.SavedRevision,
                persisted.ContentRevision,
                beforeCommit.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                beforeCommit.Document.AuxiliaryState.CharacterCreationResourcesReceipts,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                persisted.Document.AuxiliaryState.CharacterCreationResourcesReceipts,
                beforeCommit.Document,
                tamperedXml));

            var restarted = new CharacterCreationResourcesService(
                new FileWorkspaceStore(directory),
                Resolver(PrerequisiteAuthority(), ResourcesAuthority()));
            CharacterCreationResourcesState reopened = Load(restarted, id);
            Assert.AreEqual(10, reopened.PendingDraft!.KarmaInvestment);
            Assert.AreEqual(70_000m, reopened.Budget.TotalStartingNuyen);
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Available,
                restarted.LookupReceipt(new CharacterCreationResourcesReceiptLookupRequest(
                    id,
                    "android-resources-priority-001")).Outcome);
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Replayed,
                restarted.Confirm(request).Outcome);
            Assert.AreEqual(3L, new FileWorkspaceStore(directory)
                .Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    public void Purchase_domains_stale_binding_tamper_and_missing_consent_fail_closed()
    {
        string withGear = FixtureXml().Replace(
            "<sentinel>untouched</sentinel>",
            "<gears><gear><name>Existing purchase</name></gear></gears>"
            + "<sentinel>untouched</sentinel>",
            StringComparison.Ordinal);
        WithService((store, service, id, _, _) =>
        {
            CharacterCreationResourcesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToList(),
                CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
            CharacterCreationResourcesResult<CharacterCreationResourcesPreview> blocked =
                service.Preview(new CharacterCreationResourcesPreviewRequest(
                    state.Binding,
                    "karma:1"));
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, blocked.Outcome);
            Assert.AreEqual(2L, store.Get(id).Value!.ContentRevision);
        }, withGear);

        WithService((store, service, id, _, _) =>
        {
            CharacterCreationResourcesState state = Load(service, id);
            CharacterCreationResourceAllocationOption option = state.Options.Single(item =>
                item.KarmaInvestment == 1);
            CharacterCreationResourcesResult<CharacterCreationResourcesPreview> stale =
                service.Preview(new CharacterCreationResourcesPreviewRequest(
                    state.Binding with { SourceDigest = Digest('9') },
                    option.OptionId));
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, stale.Outcome);
            CollectionAssert.Contains(stale.Blockers.ToList(),
                CharacterCreationResourcesBlockers.StaleSourceDigest);
            CharacterCreationResourcesPreview preview = service.Preview(
                new CharacterCreationResourcesPreviewRequest(state.Binding, option.OptionId)).Value!;
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, service.Confirm(
                new CharacterCreationResourcesConfirmRequest(
                    state.Binding,
                    option.OptionId,
                    preview.PreviewDigest,
                    "resources-no-consent",
                    ExplicitlyConfirmed: false)).Outcome);
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, service.Confirm(
                new CharacterCreationResourcesConfirmRequest(
                    state.Binding,
                    option.OptionId,
                    Digest('8'),
                    "resources-tampered-preview",
                    ExplicitlyConfirmed: true)).Outcome);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        });
    }

    [TestMethod]
    public void Authority_exposes_exact_maximum_availability_but_does_not_claim_item_parity()
    {
        CharacterCreationResourcesAuthority authority = ResourcesAuthority();
        Assert.IsTrue(CharacterCreationResourcesRules.IsValidAuthority(authority));
        Assert.AreEqual(12, authority.MaximumAvailability);
        Assert.AreEqual(10, authority.MaximumKarmaInvestment);
        Assert.AreEqual(2_000m, authority.KarmaToNuyenRate);
        Assert.IsFalse(authority.UnrestrictedNuyen);
        Assert.IsFalse(typeof(ICharacterCreationResourcesService).GetMethods()
            .Any(method => method.Name.Contains("Gear", StringComparison.Ordinal)
                           || method.Name.Contains("Cyberware", StringComparison.Ordinal)
                           || method.Name.Contains("Vehicle", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Current_Chummer5_corpus_projects_exact_standard_priority_resource_grants()
    {
        string coreRoot = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
        ICharacterSourceDataContext context = resolver.TryCreateContext(FixtureXml())!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority prerequisite));
        Dictionary<string, decimal?> grants = prerequisite.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Resources)
            .ToDictionary(option => option.Rank, option => option.BaseResourceNuyen, StringComparer.Ordinal);
        Assert.AreEqual(450_000m, grants["A"]);
        Assert.AreEqual(275_000m, grants["B"]);
        Assert.AreEqual(140_000m, grants["C"]);
        Assert.AreEqual(50_000m, grants["D"]);
        Assert.AreEqual(6_000m, grants["E"]);
        Assert.IsTrue(context.TryResolveCreationResourcesAuthority(
            out CharacterCreationResourcesAuthority resources));
        Assert.IsTrue(CharacterCreationResourcesRules.IsValidAuthority(resources),
            string.Join(',', resources.Blockers));
        Assert.AreEqual(2_000m, resources.KarmaToNuyenRate);
        Assert.AreEqual(10, resources.MaximumKarmaInvestment);
        Assert.AreEqual(5_000m, resources.NuyenCarryover);
        Assert.AreEqual(12, resources.MaximumAvailability);
    }

    [TestMethod]
    public void Core_dependency_injection_registers_resources_authority()
    {
        ServiceCollection services = new();
        services.AddChummerHeadlessCore(AppContext.BaseDirectory, AppContext.BaseDirectory);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<CharacterCreationResourcesService>(
            provider.GetRequiredService<ICharacterCreationResourcesService>());
    }

    private static CharacterCreationResourcesState Load(
        ICharacterCreationResourcesService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationResourcesResult<CharacterCreationResourcesState> result = service.Load(
            new CharacterCreationResourcesLoadRequest(id));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void WithService(
        Action<FileWorkspaceStore, ICharacterCreationResourcesService, CharacterWorkspaceId, string, string> action,
        string? xml = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-resources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisiteAuthority = PrerequisiteAuthority();
            CharacterCreationResourcesAuthority resourcesAuthority = ResourcesAuthority();
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId id = new("resources-runner");
            string content = xml ?? FixtureXml();
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(content, RulesetDefaults.Sr5)).Success);
            ICharacterSourceDataResolver resolver = Resolver(
                prerequisiteAuthority,
                resourcesAuthority);
            var prerequisiteService = new CharacterCreationPrerequisiteService(
                store,
                new StubCharacterQueries(),
                resolver);
            CharacterCreationPrerequisiteState prerequisiteState = prerequisiteService.Load(
                new CharacterCreationPrerequisiteLoadRequest(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign(
                "A", "E", "B", "C", "D");
            var prerequisiteRequest = new CharacterCreationPrerequisitePreviewRequest(
                prerequisiteState.Binding,
                ranks)
            {
                HeritageSelectionId = "human",
                TalentSelectionId = "mundane"
            };
            CharacterCreationPrerequisitePreview prerequisitePreview = prerequisiteService.Preview(
                prerequisiteRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                prerequisiteService.Confirm(new CharacterCreationPrerequisiteConfirmRequest(
                    prerequisitePreview.Binding,
                    ranks,
                    prerequisitePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = "mundane"
                }).Outcome);
            var service = new CharacterCreationResourcesService(store, resolver);
            action(store, service, id, content, directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CharacterCreationPrerequisiteAuthority PrerequisiteAuthority() =>
        CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority,
            ["A", "B", "C", "D", "E"]);

    private static CharacterCreationResourcesAuthority ResourcesAuthority()
    {
        CharacterCreationPrerequisiteAuthority prerequisite = PrerequisiteAuthority();
        Dictionary<string, decimal> grants = new(StringComparer.Ordinal)
        {
            ["A"] = 450_000m,
            ["B"] = 275_000m,
            ["C"] = 140_000m,
            ["D"] = 50_000m,
            ["E"] = 6_000m
        };
        CharacterCreationResourcePriorityOption[] options = prerequisite.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Resources)
            .Select(option =>
            {
                var candidate = new CharacterCreationResourcePriorityOption(
                    option.SourceId,
                    option.Rank,
                    grants[option.Rank],
                    option.SourceNodeDigest,
                    option.SourceAnchorIds,
                    OptionDigest: string.Empty);
                return candidate with
                {
                    OptionDigest = CharacterCreationResourcesRules.ComputePriorityOptionDigest(candidate)
                };
            })
            .ToArray();
        var authority = new CharacterCreationResourcesAuthority(
            CharacterCreationResourcesSchemas.AuthorityV1,
            RulesetDefaults.Sr5,
            prerequisite.SettingsProfileId,
            prerequisite.BuildMethod,
            2_000m,
            10,
            5_000m,
            12,
            false,
            options,
            CharacterCreationResourcesSourceAnchors.All,
            [],
            true,
            Digest('1'),
            prerequisite.RawProfileInputsDigest,
            Digest('2'),
            Digest('3'),
            AuthorityDigest: string.Empty);
        return authority with
        {
            AuthorityDigest = CharacterCreationResourcesRules.ComputeAuthorityDigest(authority)
        };
    }

    private static ICharacterSourceDataResolver Resolver(
        CharacterCreationPrerequisiteAuthority prerequisite,
        CharacterCreationResourcesAuthority resources) =>
        new StubResolver(prerequisite, resources);

    private static string FixtureXml() =>
        "<character><name>Resources Runner</name><alias>Priority</alias>"
        + "<buildmethod>Priority</buildmethod><created>false</created>"
        + "<settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings>"
        + "<karma>25</karma><nuyen>0</nuyen><sentinel>untouched</sentinel></character>";

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }
        Assert.Fail("Could not locate the isolated Core source root.");
        return string.Empty;
    }

    private static string Digest(char value) => "sha256:" + new string(value, 64);

    private static void AssertDigest(string value) =>
        Assert.IsTrue(CharacterCreationResourcesRules.IsCanonicalDigest(value), value);

    private sealed class StubResolver(
        CharacterCreationPrerequisiteAuthority prerequisite,
        CharacterCreationResourcesAuthority resources) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext TryCreateContext(string characterXml) =>
            new StubContext(prerequisite, resources);
    }

    private sealed class StubContext(
        CharacterCreationPrerequisiteAuthority prerequisite,
        CharacterCreationResourcesAuthority resources) : ICharacterSourceDataContext
    {
        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = prerequisite;
            return true;
        }

        public bool TryResolveCreationResourcesAuthority(
            out CharacterCreationResourcesAuthority authority)
        {
            authority = resources;
            return true;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }

    private sealed class StubCharacterQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            XElement root = XDocument.Parse(document.Content).Root!;
            return new CharacterFileSummary(
                root.Element("name")?.Value ?? string.Empty,
                root.Element("alias")?.Value ?? string.Empty,
                string.Empty,
                root.Element("buildmethod")?.Value ?? string.Empty,
                string.Empty,
                string.Empty,
                25,
                0,
                false);
        }

        public CharacterValidationResult Validate(CharacterDocument document) => new(true, []);
    }

}
