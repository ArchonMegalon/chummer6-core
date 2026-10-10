using System.Reflection;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPriorityRacialTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Priority_racial_extension_preserves_existing_human_drafts(string method)
    {
        ICharacterSourceDataResolver currentResolver = null!;
        using var context = ReadyContext.Create(true, buildMethod: method, wrapResolver: inner =>
        {
            currentResolver = inner;
            return new LegacyResolver(inner);
        });
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var store = new FileWorkspaceStore(context.Directory);
        var finalizer = ReadyContext.BuildFinalizer(store, context.Queries, currentResolver);
        var state = finalizer.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanReview, string.Join(",", state.Blockers));
        var reloaded = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(original.Document.Content, reloaded.Document.Content);
        Assert.AreEqual(original.Document.AuxiliaryStateDigest, reloaded.Document.AuxiliaryStateDigest);
        Assert.AreEqual(original.ContentRevision, reloaded.ContentRevision);
        var sourceContext = currentResolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        var originalDigest = original.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!.AuthorityDigest;
        foreach (var changed in new[]
        {
            authority with { RawMetatypesXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different metatypes") },
            authority with { RawProfileInputsDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different profile") },
            authority with { CreationKarmaTotal = authority.CreationKarmaTotal + 1 }
        })
        {
            var rehashed = changed with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(changed) };
            Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility.MatchesLegacyHumanCatalog(originalDigest, rehashed));
        }
        Assert.IsTrue(sourceContext.TryResolveCreationDefaultStartingNuyen(out var cash));
        var review = finalizer.Review(new(state.Binding)
            { StartingCash = new(cash!.AuthorityDigest, cash.Dice) }).Value!;
        Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
        var command = new CharacterCreationFinalizationConfirmRequest(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, "historical-human-finalize", true) { StartingCash = review.Plan.StartingCash };
        var applied = finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
        var coldStore = new FileWorkspaceStore(context.Directory);
        var coldFinalizer = ReadyContext.BuildFinalizer(coldStore, context.Queries, currentResolver);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, coldFinalizer.Confirm(command).Outcome);
        Assert.AreEqual("Human", XElement.Parse(coldStore.Get(context.WorkspaceId).Value!.Document.Content).Element("metatype")!.Value);
    }

    [TestMethod]
    public void Priority_racial_cached_sources_fail_closed_after_quality_change_and_restore()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Elf", amendSettings: _ => { });
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative);
        string path = Path.Combine(context.Directory, "source", "data", "qualities.xml");
        string bytes = File.ReadAllText(path);
        var xml = XDocument.Parse(bytes);
        var lowLight = xml.Root!.Element("qualities")!.Elements("quality")
            .Single(item => item.Element("name")!.Value == "Low-Light Vision");
        lowLight.Element("bonus")!.Add(new XElement("unknown-effect"));
        File.WriteAllText(path, xml.ToString());
        Assert.IsFalse(sourceContext.TryResolveCreationPrerequisiteAuthority(out var changed) && changed.IsAuthoritative);
        File.WriteAllText(path, bytes);
        Assert.IsFalse(sourceContext.TryResolveCreationPrerequisiteAuthority(out var restored) && restored.IsAuthoritative,
            "A previously drifted source context must not revive after ABA restoration.");
        var freshResolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(
            Path.Combine(context.Directory, "source"), Path.Combine(context.Directory, "source"), null));
        var freshContext = freshResolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(freshContext.TryResolveCreationPrerequisiteAuthority(out var fresh));
        Assert.IsTrue(fresh.IsAuthoritative, string.Join(",", fresh.Blockers));
        Assert.AreEqual(authority.AuthorityDigest, fresh.AuthorityDigest);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, context.Store.Get(context.WorkspaceId).Value!.Document.AuxiliaryStateDigest);
    }

    [TestMethod]
    public void Priority_racial_sources_are_detached_and_rehashed_forgery_is_rejected()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Elf");
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var first));
        var firstElf = first.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.IsEnabled && item.MetatypeName == "Elf");
        var source = firstElf.RacialQualitySources!.Single();
        ((IList<string>)source.SourceAnchorIds)[0] = "caller mutation";
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var fresh));
        Assert.AreEqual(fresh.AuthorityDigest, CharacterCreationPrerequisiteAuthorityDigest.Compute(fresh));
        var actual = fresh.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.IsEnabled && item.MetatypeName == "Elf").RacialQualitySources!.Single();
        Assert.AreNotEqual("caller mutation", actual.SourceAnchorIds[0]);
        var draft = workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!;
        var forgedSource = actual with { EffectiveSourceDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("forged input") };
        forgedSource = forgedSource with { SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(
            forgedSource.EffectiveSourceDigest, forgedSource.SourceId, forgedSource.CanonicalSourceXml) };
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(forgedSource));
        var forged = draft with { HeritageSelection = draft.HeritageSelection! with { RacialQualitySources = [forgedSource] } };
        forged = forged with { DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(forged) };
        Assert.IsFalse(CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(forged, workspace.Id,
            workspace.ContentRevision, draft.BaseRawCharacterXmlDigest, fresh));
        Assert.IsTrue(CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(draft, workspace.Id,
            workspace.ContentRevision, draft.BaseRawCharacterXmlDigest, fresh));
        Assert.AreEqual(workspace.Document.AuxiliaryStateDigest, context.Store.Get(context.WorkspaceId).Value!.Document.AuxiliaryStateDigest);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("polarity")]
    [DataRow("disabled-book")]
    [DataRow("unsupported-bonus")]
    [DataRow("duplicate-field")]
    [DataRow("metatype-bonus")]
    public void Priority_racial_authority_rejects_incomplete_sources(string change)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var current));
        var baseline = LegacyAuthority(current);
        var quality = XElement.Parse(current.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.IsEnabled && item.MetatypeName == "Elf").RacialQualitySources!.Single().CanonicalSourceXml);
        var document = XDocument.Load(Path.Combine(FindCoreRoot(), "Chummer", "data", "metatypes.xml"));
        var elf = document.Root!.Element("metatypes")!.Elements("metatype").Single(row => row.Element("name")!.Value == "Elf");
        XElement[] definitions = [quality];
        string[] books = ["SR5"];
        switch (change)
        {
            case "missing": definitions = []; break;
            case "duplicate": definitions = [quality, new XElement(quality)]; break;
            case "polarity": quality.Element("category")!.Value = "Negative"; break;
            case "disabled-book": books = []; break;
            case "unsupported-bonus": quality.Element("bonus")!.Add(new XElement("unknown-effect")); break;
            case "duplicate-field": quality.Add(new XElement("karma", "0")); break;
            case "metatype-bonus": elf.Element("bonus")!.Add(new XElement("reach", 1)); break;
        }
        var result = CharacterCreationPriorityRacialAuthority.Bind(baseline, document, definitions,
            CharacterCreationMagicResonanceDigest.ComputeUtf8("test source graph"), books);
        Assert.IsTrue(result.Options.SelectMany(row => row.HeritageOptions)
            .Where(item => item.MetatypeName == "Elf" && item.MetavariantSourceId is null)
            .All(item => !item.IsEnabled && item.RacialQualitySources is null));
        Assert.AreEqual(result.AuthorityDigest, CharacterCreationPrerequisiteAuthorityDigest.Compute(result));
    }

    internal static CharacterCreationPrerequisiteAuthority LegacyAuthority(CharacterCreationPrerequisiteAuthority current)
    {
        // Reproduce the exact pre-extension catalog, not a relaxed validator.
        var baseline = current with { Options = current.Options.Select(row => row with
        { HeritageOptions = row.HeritageOptions.Select(option => option.RacialQualitySources is null ? option
            : option with { RacialQualitySources = null, IsEnabled = false,
                Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported] }).ToArray() }).ToArray() };
        return baseline with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(baseline) };
    }

    private sealed class LegacyResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            var source = inner.TryCreateContext(characterXml);
            if (source is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, LegacyContext>();
            ((LegacyContext)proxy).Inner = source;
            return proxy;
        }
    }

    public class LegacyContext : DispatchProxy
    {
        public ICharacterSourceDataContext Inner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ICharacterSourceDataContext.TryResolveCreationPrerequisiteAuthority))
            {
                bool resolved = Inner.TryResolveCreationPrerequisiteAuthority(out var authority);
                args![0] = LegacyAuthority(authority);
                return resolved;
            }
            return method.Invoke(Inner, args);
        }
    }

    private static string FindCoreRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Chummer", "data", "metatypes.xml"))) return directory.FullName;
        throw new DirectoryNotFoundException("Canonical data missing.");
    }
}
