using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr6;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6CharacterCreationBootstrapTests
{
    [TestMethod]
    [DataRow(Sr6CharacterCreationBuildMethods.Priority)]
    [DataRow(Sr6CharacterCreationBuildMethods.SumToTen)]
    [DataRow(Sr6CharacterCreationBuildMethods.PointBuy)]
    [DataRow(Sr6CharacterCreationBuildMethods.LifePath)]
    [DataRow(Sr6CharacterCreationBuildMethods.Karma)]
    public void Method_creates_an_atomic_sr6_draft_and_cold_reopens_without_sr5_sources(string method)
    {
        using var fixture = new Fixture();
        var result = fixture.Service.Create(Request(method));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        var receipt = result.Value;
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(receipt));
        Assert.AreEqual(RulesetDefaults.Sr6, receipt.Binding.RulesetId);
        Assert.AreEqual(method, receipt.Summary.BuildMethod);
        Assert.IsFalse(receipt.Summary.Created);
        Assert.AreEqual(string.Empty, receipt.Summary.Metatype);
        Assert.AreEqual(1L, receipt.ContentRevision);
        Assert.AreEqual(0L, receipt.SavedRevision);
        var stored = fixture.Store.Get(receipt.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(receipt.WorkspaceId, stored.Document));
        Assert.IsTrue(fixture.Provider.IsCurrent(receipt.WorkspaceId, stored.Document));
        var root = XDocument.Parse(stored.Document.Content).Root!;
        Assert.AreEqual("SR6", root.Element("gameedition")!.Value);
        Assert.AreEqual("Runner & Co", root.Element("name")!.Value);
        Assert.IsNull(root.Element("metatype"));
        Assert.AreEqual("0", root.Element("karma")!.Value, "No budget may be spent or granted by the pending marker.");
        Assert.AreEqual("0", root.Element("nuyen")!.Value);
        Assert.IsFalse(receipt.SourceAnchorIds.Any(anchor => anchor.EndsWith(".xml", StringComparison.Ordinal)
            || anchor.StartsWith("settings.xml", StringComparison.Ordinal)));
        Assert.AreEqual(method != Sr6CharacterCreationBuildMethods.Priority,
            receipt.SourceAnchorIds.Any(anchor => anchor.StartsWith("sr6_schattenkompendium_2022:", StringComparison.Ordinal)));

        // A fresh store and provider model process restart, not a cached object.
        var reopened = new FileWorkspaceStore(fixture.Directory).Get(receipt.WorkspaceId).Value;
        Assert.IsNotNull(reopened);
        Assert.AreEqual(stored.Document.Content, reopened.Document.Content);
        Assert.AreEqual(stored.ContentRevision, reopened.ContentRevision);
        Assert.AreEqual(stored.SavedRevision, reopened.SavedRevision);
        Assert.AreEqual(receipt.Binding.BindingDigest, reopened.Document.AuxiliaryState.CharacterCreationBootstrapBinding!.BindingDigest);
        Assert.IsTrue(new Sr6CharacterCreationBootstrapProvider().IsCurrent(receipt.WorkspaceId, reopened.Document));
        Assert.IsTrue(fixture.Codec.Validate(reopened.Document.PayloadEnvelope).IsValid);
        var saved = fixture.Store.SaveCheckpoint(receipt.WorkspaceId, reopened.ContentRevision);
        Assert.IsTrue(saved.Success, saved.Error);
        var savedReopen = new FileWorkspaceStore(fixture.Directory).Get(receipt.WorkspaceId).Value!;
        Assert.AreEqual(1L, savedReopen.ContentRevision);
        Assert.AreEqual(1L, savedReopen.SavedRevision);
        Assert.AreEqual(stored.Document.Content, savedReopen.Document.Content);
        Assert.IsTrue(fixture.Provider.IsCurrent(receipt.WorkspaceId, savedReopen.Document));
    }

    [TestMethod]
    [DataRow(Sr6CharacterCreationBuildMethods.Priority)]
    [DataRow(Sr6CharacterCreationBuildMethods.SumToTen)]
    [DataRow(Sr6CharacterCreationBuildMethods.PointBuy)]
    [DataRow(Sr6CharacterCreationBuildMethods.LifePath)]
    [DataRow(Sr6CharacterCreationBuildMethods.Karma)]
    public void Sr6_creation_keeps_owner_admission_and_cold_storage_isolation(string method)
    {
        using var fixture = new Fixture();
        using var owner = new RequestOwnerContextAccessor(new OwnerScope("sr6-owner-a"));
        using var other = new RequestOwnerContextAccessor(new OwnerScope("sr6-owner-b"));
        var service = new OwnerBoundCharacterCreationBootstrapService(fixture.Service, owner);
        var stamp = owner.Capture();
        Assert.IsNull(service.Create(other.Capture(), Request(method)).Value);
        var created = service.Create(stamp, Request(method));
        Assert.IsNotNull(created.Value, string.Join(",", created.Blockers));
        var id = created.Value.WorkspaceId;
        Assert.IsFalse(fixture.Store.Get(id).Success);
        Assert.IsFalse(fixture.Store.Get(other.Current, id).Success);
        var cold = new FileWorkspaceStore(fixture.Directory).Get(owner.Current, id).Value;
        Assert.IsNotNull(cold);
        Assert.IsTrue(fixture.Provider.IsCurrent(id, cold.Document));
        owner.Dispose();
        Assert.IsNull(service.Create(stamp, Request(method)).Value);
        Assert.HasCount(1, fixture.Store.List(stamp.Owner));
    }

    [TestMethod]
    public void Cross_edition_and_cross_method_profiles_are_rejected_without_writes()
    {
        using var fixture = new Fixture();
        foreach (string method in Sr6CharacterCreationBuildMethods.All)
        {
            var request = Request(method);
            foreach (string profile in Sr6CharacterCreationBuildMethods.All.Where(other => other != method)
                         .Select(other => Request(other).SettingsProfileId)
                         .Concat([CharacterCreationBootstrapProfiles.PrioritySettingsProfileId,
                             CharacterCreationBootstrapProfiles.SumToTenSettingsProfileId,
                             CharacterCreationBootstrapProfiles.KarmaSettingsProfileId,
                             CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId]))
                Assert.IsNull(fixture.Service.Create(request with { SettingsProfileId = profile }).Value);
            foreach (var invalid in new[]
            {
                request with { RulesetId = RulesetDefaults.Sr5 },
                request with { RulesetId = "SR6" },
                request with { BuildMethod = "LifeModules" },
                request with { BuildMethod = "SumToTen" },
                request with { Schema = "unknown" },
                request with { Stage = "complete" },
                request with { Name = " " },
                request with { Alias = new string('x', 257) }
            }) Assert.IsNull(fixture.Service.Create(invalid).Value);
        }
        Assert.HasCount(0, fixture.Store.List());
    }

    [TestMethod]
    public void Missing_or_ambiguous_sr6_provider_never_falls_back_to_sr5_or_creates_a_workspace()
    {
        using var fixture = new Fixture();
        foreach (var providers in new IRulesetCharacterCreationBootstrapProvider[][]
                 { [], [fixture.Provider, new Sr6CharacterCreationBootstrapProvider()] })
        {
            var service = fixture.CreateService(providers);
            var result = service.Create(Request(Sr6CharacterCreationBuildMethods.Priority));
            Assert.IsNull(result.Value);
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Unavailable, result.Outcome);
        }
        Assert.HasCount(0, fixture.Store.List());
    }

    [TestMethod]
    public void Sr6_activation_returns_a_committed_receipt_for_reload_not_a_fabricated_sr5_bundle()
    {
        using var fixture = new Fixture();
        var result = fixture.Service.CreateActivation(Request(Sr6CharacterCreationBuildMethods.Priority));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(result.Receipt));
        Assert.IsNull(result.Bundle);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.HasCount(1, fixture.Store.List());
    }

    [TestMethod]
    public void Redigest_cannot_admit_a_foreign_profile_source_or_preselected_foundation()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Service.Create(Request(Sr6CharacterCreationBuildMethods.Priority)).Value!;
        var document = fixture.Store.Get(receipt.WorkspaceId).Value!.Document;
        foreach (var changed in new[]
        {
            receipt.Binding with { RawProfileInputsDigest = "sha256:" + new string('1', 64) },
            receipt.Binding with { MetatypeAuthorityDigest = "sha256:" + new string('1', 64) },
            receipt.Binding with { PrerequisiteAuthorityDigest = "sha256:" + new string('1', 64) },
            receipt.Binding with { SettingsProfileId = CharacterCreationBootstrapProfiles.PrioritySettingsProfileId },
            receipt.Binding with { SourceAnchorIds = ["metatypes.xml", "priorities.xml"] }
        })
        {
            var rebound = changed with { BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(changed) };
            var tampered = document with { State = document.State with
                { AuxiliaryState = document.AuxiliaryState with { CharacterCreationBootstrapBinding = rebound } } };
            Assert.IsFalse(fixture.Provider.IsCurrent(receipt.WorkspaceId, tampered));
        }
        foreach (string addition in new[] { "<metatype>Human</metatype>", "<prioritymetatype>A</prioritymetatype>", "<lifemodules />" })
        {
            string xml = document.Content.Replace("</character>", addition + "</character>", StringComparison.Ordinal);
            var tampered = new WorkspaceDocument(fixture.Codec.WrapImport(RulesetDefaults.Sr6,
                new WorkspaceImportDocument(xml, RulesetDefaults.Sr6)), WorkspaceDocumentFormat.NativeXml);
            Assert.IsFalse(fixture.Provider.TryPrepareBinding(receipt.WorkspaceId, tampered, out _, out _, out _));
        }
        Assert.HasCount(1, fixture.Store.List());
    }

    [TestMethod]
    public void Sr6_registration_is_idempotent_and_does_not_install_an_sr5_source_provider()
    {
        var services = new ServiceCollection();
        services.AddSr6Ruleset().AddSr6Ruleset();
        using var provider = services.BuildServiceProvider();
        var implementations = provider.GetServices<IRulesetCharacterCreationBootstrapProvider>().ToArray();
        Assert.HasCount(1, implementations);
        Assert.AreEqual(RulesetDefaults.Sr6, implementations[0].RulesetId);
    }

    [TestMethod]
    public void Sr6_pending_marker_cannot_bypass_bootstrap_through_generic_import()
    {
        using var fixture = new Fixture();
        var receipt = fixture.Service.Create(Request(Sr6CharacterCreationBuildMethods.Priority)).Value!;
        var document = fixture.Store.Get(receipt.WorkspaceId).Value!.Document;
        var workspace = new WorkspaceService(fixture.Store, new RulesetWorkspaceCodecResolver([fixture.Codec]),
            new WorkspaceImportRulesetDetector());
        Assert.ThrowsExactly<InvalidOperationException>(() => workspace.Import(
            new WorkspaceImportDocument(document.Content, RulesetDefaults.Sr6)));
        Assert.HasCount(1, fixture.Store.List());
    }

    private static CharacterCreationBootstrapRequest Request(string method)
    {
        Assert.IsTrue(Sr6CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out var id));
        return new(CharacterCreationBootstrapSchemas.RequestV1, CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            RulesetDefaults.Sr6, " Runner & Co ", " Alias ", method, id);
    }

    private sealed class RejectSr5SourceResolver : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => throw new AssertFailedException("SR6 must never resolve SR5 settings.xml sources.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "chummer-sr6-bootstrap-" + Guid.NewGuid().ToString("N"));
        public FileWorkspaceStore Store { get; }
        public Sr6CharacterCreationBootstrapProvider Provider { get; } = new();
        public ICharacterFileQueries Queries { get; } = new XmlCharacterFileQueries(new CharacterFileService());
        public Sr6WorkspaceCodec Codec { get; }
        public CharacterCreationBootstrapService Service { get; }

        public Fixture()
        {
            Store = new FileWorkspaceStore(Directory);
            Codec = new(Queries, new XmlCharacterSectionQueries(new CharacterSectionService()),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            Service = CreateService([Provider]);
        }

        public CharacterCreationBootstrapService CreateService(IEnumerable<IRulesetCharacterCreationBootstrapProvider> providers)
            => new(Store, new RulesetWorkspaceCodecResolver([Codec]), Queries, new RejectSr5SourceResolver(),
                rulesetBootstrapProviders: providers);

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
