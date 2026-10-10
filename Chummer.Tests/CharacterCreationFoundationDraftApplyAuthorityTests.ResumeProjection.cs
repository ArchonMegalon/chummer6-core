using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(5)]
    public void Foundation_resume_subset_keeps_exact_state_and_full_next_module_choices(int count)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("foundation-resume-subset");
            var store = SeedJourney(directory, id);
            string[] modules = [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId];
            AppendSequence(CreateService(store), id, modules.Take(count).ToArray());
            byte[] saved = File.ReadAllBytes(WorkspacePath(directory, id));
            var restarted = new FileWorkspaceStore(directory);
            var catalog = new ObservedFoundationCatalog(CreateCatalog());
            CharacterCreationFoundationService Service(ILifeModulesCatalogService source) => new(
                restarted, new XmlCharacterFileQueries(new CharacterFileService()),
                new FileSystemCharacterSourceDataResolver(CreateOverlays()), source,
                new CharacterCreationFoundationDraftApplyAuthority(restarted));
            var service = Service(catalog);
            var actual = Load(service, id);
            Assert.AreEqual(1, catalog.FoundationCalls);
            Assert.AreEqual(0, catalog.FullCalls);
            CollectionAssert.AreEquivalent(new[] { TirModuleId }.Concat(modules.Take(count)).ToArray(),
                catalog.SelectedIds!);
            Assert.IsTrue(actual.LifeModuleBudget.IsExact);
            var baseline = Service(new ObservedFoundationCatalog(CreateCatalog(), useFullCatalog: true));
            Assert.AreEqual(JsonSerializer.Serialize(Load(baseline, id)), JsonSerializer.Serialize(actual),
                "Budget, draft, source bindings, blockers and display digest must remain byte-identical.");
            int beforeJourney = catalog.FoundationCalls;
            Assert.AreEqual(JsonSerializer.Serialize(JourneyState(baseline, id)),
                JsonSerializer.Serialize(JourneyState(service, id)));
            Assert.AreEqual(beforeJourney, catalog.FoundationCalls, "Next choices need the complete catalog.");
            Assert.IsTrue(catalog.FullCalls > 0);
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ObservedFoundationCatalog(ILifeModulesCatalogService inner, bool useFullCatalog = false)
        : ILifeModulesCatalogService
    {
        public int FoundationCalls { get; private set; }
        public int FullCalls { get; private set; }
        public string[]? SelectedIds { get; private set; }
        public LifeModuleCatalogAuthorityDto GetAuthority() => inner.GetAuthority();
        public byte[]? ReadSourceBytes(string digest) => inner.ReadSourceBytes(digest);
        public IReadOnlyList<LifeModuleStageDto> GetStages() => inner.GetStages();
        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null) => inner.GetModules(stage);
        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            FullCalls++;
            return inner.GetOptionProjections(stage, enabledSources);
        }
        public IReadOnlyList<LifeModuleLegalOptionDto> GetFoundationOptionProjections(
            IReadOnlyCollection<string> selectedModuleIds, IReadOnlyCollection<string>? enabledSources = null)
        {
            FoundationCalls++;
            SelectedIds = selectedModuleIds.ToArray();
            return useFullCatalog ? inner.GetOptionProjections(enabledSources: enabledSources)
                : inner.GetFoundationOptionProjections(selectedModuleIds, enabledSources);
        }
    }
}
