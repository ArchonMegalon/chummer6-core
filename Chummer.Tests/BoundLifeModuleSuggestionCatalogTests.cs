using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class BoundLifeModuleSuggestionCatalogTests
{
    [TestMethod]
    public void Default_catalog_uses_the_foundation_sources_and_preserves_all_prompt_anchors()
    {
        var fixture = new CatalogFixture();
        var foundation = fixture.Foundation(fixture.Stock);
        var projection = fixture.Project(fixture.Stock, foundation);
        Assert.IsNotNull(projection);
        Assert.AreEqual(fixture.Stock.GetAuthority().RawXmlDigest, projection.CatalogSourceDigest);
        Assert.AreEqual(foundation.SnapshotDigest, projection.FoundationSnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(foundation.NationalityOptions),
            JsonSerializer.Serialize(projection.Options.Where(m => m.StageOrder == 1).ToArray()));
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Stock.GetOptionProjections(
            enabledSources: foundation.Binding.EnabledSources)), JsonSerializer.Serialize(projection.Options));
        Assert.IsTrue(projection.Options.Any(m => m.StageOrder > 1));
        Assert.IsTrue(projection.Options.SelectMany(m => m.FollowUps.Concat(m.Versions.SelectMany(v => v.FollowUps)))
            .Any(prompt => prompt.SourceAnchorIds.Count > 0));
        Assert.IsFalse(ReferenceEquals(projection.Options, fixture.Stock.GetOptionProjections(
            enabledSources: foundation.Binding.EnabledSources)), "Do not expose the catalog cache's mutable DTO arrays.");
    }

    [TestMethod]
    public void Supported_retained_nationality_change_uses_exact_bytes_and_leaves_other_runner_unchanged()
    {
        var fixture = new CatalogFixture();
        var other = fixture.Foundation(fixture.Stock);
        string otherBefore = JsonSerializer.Serialize(fixture.Project(fixture.Stock, other));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(fixture.Bytes));
        var nationality = xml.Descendants("module").First(m => (string?)m.Element("stage") == "Nationality");
        string id = nationality.Element("id")!.Value;
        nationality.Element("name")!.Value = "Retained regional identity";
        var amended = new XmlLifeModulesCatalogService(Encoding.UTF8.GetBytes(xml.ToString()));
        var foundation = fixture.Foundation(amended);
        Assert.AreNotEqual(other.Binding.SourceDigest, foundation.Binding.SourceDigest);
        Assert.AreNotEqual(JsonSerializer.Serialize(other.NationalityOptions), JsonSerializer.Serialize(foundation.NationalityOptions));
        var projected = fixture.Project(amended, foundation);
        Assert.IsNotNull(projected);
        Assert.AreEqual("Retained regional identity", projected.Options.Single(m => m.ModuleId == id).Name);
        Assert.AreEqual(amended.GetAuthority().RawXmlDigest, projected.CatalogSourceDigest);
        Assert.AreEqual(JsonSerializer.Serialize(amended.GetOptionProjections(enabledSources: foundation.Binding.EnabledSources)),
            JsonSerializer.Serialize(projected.Options));
        Assert.IsNull(fixture.Project(fixture.Stock, foundation), "A registered stock catalog cannot replace the bound amended input.");
        Assert.IsNull(fixture.Project(amended, other), "The old source fingerprint must not admit the amended catalog.");
        Assert.AreEqual(otherBefore, JsonSerializer.Serialize(fixture.Project(fixture.Stock, other)));
    }

    [TestMethod]
    public void Equal_nationalities_do_not_admit_a_different_later_stage_source()
    {
        var fixture = new CatalogFixture();
        var original = fixture.Foundation(fixture.Stock);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(fixture.Bytes));
        xml.Descendants("module").First(m => (string?)m.Element("stage") != "Nationality")
            .Element("name")!.Value = "Retained later-stage name";
        var amended = new XmlLifeModulesCatalogService(Encoding.UTF8.GetBytes(xml.ToString()));
        var current = fixture.Foundation(amended);
        Assert.AreEqual(JsonSerializer.Serialize(original.NationalityOptions), JsonSerializer.Serialize(current.NationalityOptions));
        Assert.AreNotEqual(original.Binding.SourceDigest, current.Binding.SourceDigest);
        Assert.IsNull(fixture.Project(amended, original));
        Assert.IsNotNull(fixture.Project(amended, current));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Admitted_current_execution_reads_its_retained_catalog_and_rejects_a_foreign_capture(bool amended)
    {
        var stock = new CatalogFixture(currentEnvironment: true);
        string directory = Path.Combine(Path.GetTempPath(), "life-catalog9733-amend-" + Guid.NewGuid().ToString("N"));
        try
        {
            CatalogFixture.PrepareAmendedSource(directory);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(stock.Bytes));
            var nationality = new XElement(xml.Descendants("module").First(m => (string?)m.Element("stage") == "Nationality"));
            string id = nationality.Element("id")!.Value;
            nationality.Element("name")!.Value = "Retained regional identity";
            var later = new XElement(xml.Descendants("module").First(m => (string?)m.Element("stage") != "Nationality"));
            string laterId = later.Element("id")!.Value;
            later.Element("name")!.Value = "Retained later-stage choice";
            // Current admission consumes character-selected custom data. An enabled
            // content overlay is a different source mode and is deliberately refused.
            new XDocument(new XElement("chummer", new XElement("modules",
                new XElement("module", new XElement("id", id), new XElement("name", nationality.Element("name")!.Value)),
                new XElement("module", new XElement("id", laterId), new XElement("name", later.Element("name")!.Value)))))
                .Save(Path.Combine(directory, "customdata", "regional", "amend_lifemodules.xml"));
            var fixture = new CatalogFixture(amended ? directory : null, currentEnvironment: true);
            var source = fixture.Resolver.TryCreateContext(fixture.Workspace.Document.Content);
            Assert.IsNotNull(source);
            Assert.IsTrue(source.TryResolveCareerSettingsProfile(out var profileXml, out _));
            var profile = XDocument.Parse(profileXml).Root!;
            Assert.IsNull(profile.Element("maxskillrating"), "Exercise the inherited Career active-skill cap.");
            Assert.IsNull(profile.Element("maxknowledgeskillrating"), "Exercise the inherited Career knowledge-skill cap.");
            Assert.IsNotNull(profile.Element("allowskilldicerolling"));
            Assert.IsNotNull(profile.Element("freemartialartspecialization"));
            Assert.IsNotNull(profile.Element("karmacost")!.Element("karmacomplexformskillsoft"));
            Assert.IsTrue(fixture.Resolver.TryCaptureChummer5aRuleSources(fixture.Workspace.Document.Content,
                out var capture, out var diagnostics), JsonSerializer.Serialize(diagnostics));
            Assert.IsNotNull(capture);
            Assert.AreEqual(Chummer5aLegacyImportCompatibility.ProfileV1, capture.CompatibilityProfile);
            if (amended)
            {
                Assert.AreEqual(CatalogFixture.RegionalVersion, capture.RequiredRulePacks
                    .Single(pack => pack.Id == "chummer5a:" + CatalogFixture.RegionalManifestId).Version);
                Assert.IsTrue(diagnostics.Any(row => row.MessageKey == "chummer5a-import.amend-semantics-unverified"
                    && row.Severity == RulePackResolutionSeverityLevels.Warning), JsonSerializer.Serialize(diagnostics));
                // Current V7 admission observes the bounded selected-source grammar;
                // retaining an amendment does not grant historical execution equivalence.
                Assert.IsNull(capture.CreateExecutionResolution());
            }
            var admission = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(
                fixture.Workspace, capture, fixture.Queries);
            Assert.IsNotNull(admission.Execution, string.Join(",", admission.Blockers));
            var execution = admission.Execution;
            var state = execution.LoadLifeModuleFinalization(fixture.Workspace, capture, fixture.Queries)?.Value;
            Assert.IsNotNull(state);
            var projection = execution.LoadLifeModuleSuggestionCatalog(fixture.Workspace, capture,
                fixture.Queries, state, "admitted-environment");
            Assert.IsNotNull(projection);
            Assert.AreEqual(state.Binding.SourceDigest, projection.Binding.SourceDigest);
            if (amended)
            {
                Assert.AreEqual("Retained regional identity", projection.Options.Single(m => m.ModuleId == id).Name);
                Assert.AreEqual("Retained later-stage choice", projection.Options.Single(m => m.ModuleId == laterId).Name);
                Assert.IsTrue(projection.Options.Single(m => m.ModuleId == laterId).StageOrder > 1);
                Assert.AreNotEqual(stock.Stock.GetAuthority().RawXmlDigest, projection.CatalogSourceDigest);
                Assert.IsNull(stock.Project(stock.Stock, state));
            }
            else Assert.AreEqual(stock.Stock.GetAuthority().RawXmlDigest, projection.CatalogSourceDigest);

            Assert.IsTrue(stock.Resolver.TryCaptureChummer5aRuleSources(stock.Workspace.Document.Content.Replace(
                "Catalog fixture", "Another runner", StringComparison.Ordinal), out var foreign, out var foreignDiagnostics),
                JsonSerializer.Serialize(foreignDiagnostics));
            Assert.IsNotNull(foreign);
            Assert.IsNull(execution.LoadLifeModuleSuggestionCatalog(fixture.Workspace, foreign,
                fixture.Queries, state, "admitted-environment"));
            Assert.IsNull(execution.LoadLifeModuleSuggestionCatalog(fixture.Workspace with { ContentRevision = 2 }, capture,
                fixture.Queries, state, "admitted-environment"));

            // Full-profile support requires the same explicit capability as the
            // native bootstrap; older retained interpretation profiles stay closed.
            var unmarkedXml = XDocument.Parse(fixture.Workspace.Document.Content);
            unmarkedXml.Root!.Element(CharacterCreationLifestyleMechanicsRules.CapabilityElement)!.Remove();
            var unmarked = fixture.Workspace with { Document = new WorkspaceDocument(unmarkedXml.ToString(), "sr5") };
            Assert.IsTrue(fixture.Resolver.TryCaptureChummer5aRuleSources(unmarked.Document.Content,
                out var unmarkedCapture, out var unmarkedDiagnostics), JsonSerializer.Serialize(unmarkedDiagnostics));
            Assert.IsNotNull(unmarkedCapture);
            Assert.IsNull(CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(
                unmarked, unmarkedCapture, fixture.Queries).Execution);

            // An unrecognized selected-profile field cannot be admitted just
            // because it belongs to a known Life Modules preset.
            var unsupportedProfile = new XElement("setting", new XElement("id", profile.Element("id")!.Value),
                new XElement("unknownlifeprotocol9749", "True"));
            new XDocument(new XElement("chummer", new XElement("settings", unsupportedProfile)))
                .Save(Path.Combine(directory, "customdata", "regional", "amend_settings.xml"));
            var unsupported = new CatalogFixture(directory, currentEnvironment: true);
            var unsupportedSource = unsupported.Resolver.TryCreateContext(unsupported.Workspace.Document.Content);
            Assert.IsNotNull(unsupportedSource);
            Assert.IsTrue(unsupportedSource.TryResolveCareerSettingsProfile(out var unsupportedXml, out _));
            Assert.IsNotNull(XDocument.Parse(unsupportedXml).Root!.Element("unknownlifeprotocol9749"),
                "The negative case must reach the selected profile through the real amendment path.");
            Assert.IsTrue(unsupported.Resolver.TryCaptureChummer5aRuleSources(unsupported.Workspace.Document.Content,
                out var unsupportedCapture, out var unsupportedDiagnostics), JsonSerializer.Serialize(unsupportedDiagnostics));
            Assert.IsNotNull(unsupportedCapture);
            Assert.IsNull(CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(
                unsupported.Workspace, unsupportedCapture, unsupported.Queries).Execution);
            Assert.IsNotNull(execution.LoadLifeModuleSuggestionCatalog(fixture.Workspace,
                capture, fixture.Queries, state, "admitted-environment"),
                "A changed ambient source must not replace the admitted retained catalog.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Projection_refuses_stale_workspace_revision_edition_and_missing_source_authority()
    {
        var fixture = new CatalogFixture();
        var state = fixture.Foundation(fixture.Stock);
        Assert.IsNull(fixture.Project(fixture.Stock, state with { Binding = state.Binding with { ContentRevision = 2 } }));
        Assert.IsNull(fixture.Project(fixture.Stock, state with { Binding = state.Binding with { SavedRevision = 2 } }));
        Assert.IsNull(fixture.Project(fixture.Stock, state with { Binding = state.Binding with { WorkspaceId = new("other-runner") } }));
        Assert.IsNull(fixture.Project(fixture.Stock, state with { SnapshotDigest = "sha256:" + new string('0', 64) }));
        Assert.IsNull(fixture.Project(fixture.Stock, state, fixture.Workspace with
        { Document = new WorkspaceDocument(fixture.Workspace.Document.Content, "sr6") }));
        Assert.IsNull(OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeSuggestionCatalog(
            fixture.Workspace, fixture.Queries, new MissingSources(), fixture.Stock, state, "environment-a"));
    }

    [TestMethod]
    public void Owner_bound_API_rechecks_owner_and_expected_environment_and_never_falls_back()
    {
        var fixture = new CatalogFixture();
        var foundation = fixture.Foundation(fixture.Stock);
        var projection = fixture.Project(fixture.Stock, foundation)!;
        using var owners = new RequestOwnerContextAccessor(new OwnerScope("catalog9733-owner"));
        var store = DispatchProxy.Create<ICatalogStore, CatalogStoreProbe>();
        var probe = (CatalogStoreProbe)store;
        probe.Result = projection;
        var reader = new OwnerBoundCharacterCreationLifeModuleFinalizationService(store, owners,
            fixture.Queries, fixture.Resolver, fixture.Stock);
        var owner = owners.Capture();
        Assert.IsNotNull(reader.LoadSuggestionCatalog(owner, foundation, projection.EnvironmentDigest));
        Assert.AreEqual(owner, probe.LastOwner);
        Assert.IsNull(reader.LoadSuggestionCatalog(owner, foundation, "different-environment"));
        probe.Result = projection with { EnvironmentDigest = "changed-environment" };
        Assert.IsNull(reader.LoadSuggestionCatalog(owner, foundation, projection.EnvironmentDigest));
        Assert.IsNull(reader.LoadSuggestionCatalog(owner with { TransitionRevision = owner.TransitionRevision + 1 }, foundation));
        using var foreign = new RequestOwnerContextAccessor(new OwnerScope("catalog9733-other-owner"));
        Assert.IsNull(reader.LoadSuggestionCatalog(foreign.Capture(), foundation));
        probe.Result = null;
        Assert.IsNull(reader.LoadSuggestionCatalog(owner, foundation), "Unavailable retained source must not select Stock.");
        probe.Fail = true;
        Assert.IsNull(reader.LoadSuggestionCatalog(owner, foundation), "Unsupported source must not select Stock.");
        Assert.AreEqual(0, probe.UnrelatedCalls);
    }

    [TestMethod]
    public void Real_store_read_observes_current_record_and_does_not_touch_another_runner()
    {
        var fixture = new CatalogFixture();
        string directory = Path.Combine(Path.GetTempPath(), "life-catalog9733-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var owners = new RequestOwnerContextAccessor(new OwnerScope("catalog9733-store-owner"));
            var store = new FileWorkspaceStore(directory);
            var first = store.CreateWorkspaceDocument(owners.Current, fixture.Workspace.Document);
            var second = store.CreateWorkspaceDocument(owners.Current, fixture.Workspace.Document);
            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);
            var id = first.Entry!.Value.Id;
            var otherId = second.Entry!.Value.Id;
            string otherBefore = JsonSerializer.Serialize(store.Get(owners.Current, otherId).Value);
            var reader = new OwnerBoundCharacterCreationLifeModuleFinalizationService(store, owners,
                fixture.Queries, fixture.Resolver, fixture.Stock);
            var state = reader.Load(owners.Capture(), id).Value;
            Assert.IsNotNull(state);
            var projection = reader.LoadSuggestionCatalog(owners.Capture(), state);
            Assert.IsNotNull(projection);
            Assert.IsNull(reader.LoadSuggestionCatalog(owners.Capture(), state, "wrong-environment"));
            Assert.IsNull(reader.LoadSuggestionCatalog(owners.Capture(), state with
            { Binding = state.Binding with { ContentRevision = state.Binding.ContentRevision + 1 } }));
            Assert.IsNotNull(reader.LoadSuggestionCatalog(owners.Capture(), state, projection.EnvironmentDigest));
            Assert.AreEqual(otherBefore, JsonSerializer.Serialize(store.Get(owners.Current, otherId).Value));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public interface ICatalogStore : IWorkspaceStore, ICharacterCreationCurrentLifeModuleFinalizationCapability { }

    public class CatalogStoreProbe : DispatchProxy
    {
        public CharacterCreationLifeModuleCatalogProjection? Result;
        public OwnerContextStamp LastOwner;
        public bool Fail;
        public int UnrelatedCalls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ICharacterCreationCurrentLifeModuleFinalizationCapability.LoadCurrentLifeModuleSuggestionCatalog))
            {
                LastOwner = (OwnerContextStamp)args![1]!;
                if (Fail) throw new InvalidOperationException("unsupported-source");
                return Result;
            }
            UnrelatedCalls++;
            throw new InvalidOperationException("Unexpected store operation.");
        }
    }

    private sealed class MissingSources : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml) => null;
    }

    private sealed class CatalogFixture
    {
        public const string RegionalManifestId = "97330000-0000-4000-8000-000000000001";
        public const string RegionalVersion = "1.0";
        public byte[] Bytes { get; }
        public XmlLifeModulesCatalogService Stock { get; }
        public FileSystemCharacterSourceDataResolver Resolver { get; }
        public XmlCharacterFileQueries Queries { get; } = new(new CharacterFileService());
        public WorkspaceStoredDocument Workspace { get; }

        public CatalogFixture(string? sourceRoot = null, bool currentEnvironment = false)
        {
            string root = sourceRoot ?? ContentRoot();
            Bytes = File.ReadAllBytes(Path.Combine(root, "data", "lifemodules.xml"));
            Stock = new XmlLifeModulesCatalogService(Bytes);
            Resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
            var profile = XDocument.Load(Path.Combine(root, "data", "settings.xml")).Descendants("setting")
                .First(s => (string?)s.Element("buildmethod") == CharacterCreationBuildMethods.LifeModules);
            string settings = profile.Element("id")!.Value;
            string xml = $"<character><name>Catalog fixture</name><metatype>Human</metatype><buildmethod>LifeModule</buildmethod>"
                + $"<settings>{settings}</settings><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>"
                + "<created>False</created><karma>0</karma><nuyen>0</nuyen><gameedition>SR5</gameedition>"
                // CharacterCreationBootstrapService writes this before the first
                // capture. Never add it to an already retained environment.
                + (currentEnvironment ? new XElement(CharacterCreationLifestyleMechanicsRules.CapabilityElement,
                    CharacterCreationLifestyleMechanicsRules.Capability).ToString() : string.Empty)
                + (sourceRoot is null ? string.Empty : new XElement("customdatadirectorynames",
                    profile.Element("customdatadirectorynames")!.Elements("customdatadirectoryname")
                        .Where(row => (bool?)row.Element("enabled") == true)
                        .OrderBy(row => (int?)row.Element("order"))
                        .Select(row => new XElement("directoryname", row.Element("directoryname")!.Value))).ToString())
                + "</character>";
            Workspace = new(new CharacterWorkspaceId("catalog9733-runner"), new WorkspaceDocument(xml, "sr5"),
                1, 0, DateTimeOffset.UnixEpoch);
        }

        public static void PrepareAmendedSource(string root)
        {
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            foreach (string file in Directory.GetFiles(Path.Combine(ContentRoot(), "data"), "*.xml"))
                File.Copy(file, Path.Combine(data, Path.GetFileName(file)));
            Directory.CreateDirectory(Path.Combine(root, "customdata", "regional"));
            // The profile and saved character select the directory name; capture
            // resolves and retains this exact package GUID/version from its manifest.
            new XDocument(new XElement("manifest", new XElement("guid", RegionalManifestId),
                new XElement("version", RegionalVersion)))
                .Save(Path.Combine(root, "customdata", "regional", "manifest.xml"));
            string path = Path.Combine(data, "settings.xml");
            var settings = XDocument.Load(path);
            var profile = settings.Descendants("setting")
                .First(s => (string?)s.Element("buildmethod") == CharacterCreationBuildMethods.LifeModules);
            profile.Element("customdatadirectorynames")!.Add(new XElement("customdatadirectoryname",
                new XElement("directoryname", "regional"), new XElement("order", "0"), new XElement("enabled", "True")));
            settings.Save(path);
        }

        public CharacterCreationFoundationState Foundation(ILifeModulesCatalogService catalog)
        {
            var loaded = OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeState(Workspace, Queries, Resolver, catalog);
            Assert.IsNotNull(loaded.Value);
            Assert.AreEqual(0, loaded.Blockers.Count, string.Join(",", loaded.Blockers));
            return loaded.Value;
        }

        public CharacterCreationLifeModuleCatalogProjection? Project(ILifeModulesCatalogService catalog,
            CharacterCreationFoundationState state, WorkspaceStoredDocument? workspace = null)
            => OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeSuggestionCatalog(
                workspace ?? Workspace, Queries, Resolver, catalog, state, "environment-a");

        private static string ContentRoot()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                    foreach (string candidate in new[] { Path.Combine(directory.FullName, "Chummer"),
                        Path.Combine(directory.FullName, "core", "Chummer") })
                        if (File.Exists(Path.Combine(candidate, "data", "lifemodules.xml"))) return candidate;
            throw new DirectoryNotFoundException("Core test content root with Chummer/data is required.");
        }
    }
}
