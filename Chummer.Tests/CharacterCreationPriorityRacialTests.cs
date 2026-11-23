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
        var baseline = HumanOnlyLegacyAuthority(current);
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

    [TestMethod]
    public void Priority_racial_dwarf_requires_exact_nonnull_lifestyle_surcharge()
    {
        // Gen3 C4: admitted standard Dwarf rows must carry the claimed exact
        // 20% lifestyle surcharge as a NON-null projection. An empty bonus
        // element must fail closed (no null-vacuous admission), while
        // Human/Elf/Ork keep their legitimate null projections.
        var document = XDocument.Load(Path.Combine(FindCoreRoot(), "Chummer", "data", "metatypes.xml"));
        var root = document.Root!.Element("metatypes")!;
        var dwarf = root.Elements("metatype")
            .Single(row => row.Element("name")!.Value == "Dwarf");
        Assert.IsTrue(CharacterCreationMetatypeCatalogProjector.TryProjectPriorityRacialOption(
            dwarf, ["SR5"], out var dwarfOption));
        Assert.IsTrue(dwarfOption.IsEnabled, string.Join(",", dwarfOption.Blockers));
        Assert.AreEqual(0, dwarfOption.Blockers.Count);
        var bonuses = dwarfOption.BaseBonuses;
        Assert.IsNotNull(bonuses);
        Assert.AreEqual(20, bonuses.LifestyleCostPercent);
        Assert.AreEqual(0, bonuses.Armor);
        Assert.AreEqual(0, bonuses.Reach);

        var emptied = new XElement(dwarf);
        emptied.Element("bonus")!.ReplaceWith(new XElement("bonus"));
        Assert.IsFalse(CharacterCreationMetatypeCatalogProjector.TryProjectPriorityRacialOption(
            emptied, ["SR5"], out var emptiedOption));
        if (emptiedOption is not null)
        {
            Assert.IsFalse(emptiedOption.IsEnabled);
            Assert.IsNull(emptiedOption.BaseBonuses);
            Assert.IsTrue(emptiedOption.Blockers.Contains(
                CharacterCreationMetatypeCatalogBlockers.SpecialSemanticsUnsupported),
                string.Join(",", emptiedOption.Blockers));
        }

        // Task10011 restores published Priority admission while retaining the
        // phone's broader parser. Unsupported or malformed source effects must
        // still be refused before an authority can carry BaseBonuses.
        foreach (string invalidBonus in new[]
        {
            "<bonus><lifestylecost>-20</lifestylecost></bonus>",
            "<bonus><lifestylecost>21</lifestylecost></bonus>",
            "<bonus><lifestylecost>20</lifestylecost><armor group='0'>1</armor></bonus>",
            "<bonus><lifestylecost>20</lifestylecost><reach>1</reach></bonus>",
            "<bonus><lifestylecost>20</lifestylecost><unknown>1</unknown></bonus>",
            "<bonus><lifestylecost>20</lifestylecost><lifestylecost>20</lifestylecost></bonus>",
            "<bonus><lifestylecost>20</lifestylecost><conditionmonitor><overflow>1</overflow></conditionmonitor></bonus>"
        })
        {
            var changed = new XElement(dwarf);
            changed.Element("bonus")!.ReplaceWith(XElement.Parse(invalidBonus));
            Assert.IsFalse(CharacterCreationMetatypeCatalogProjector.TryProjectPriorityRacialOption(
                changed, ["SR5"], out _), invalidBonus);
        }
        Assert.IsFalse(CharacterCreationMetatypeCatalogProjector.IsDwarfLifestyleSurcharge(
            new(0, 0, 20) { ConditionOverflow5497 = 1 }));

        foreach (string name in new[] { "Elf", "Ork" })
        {
            var row = root.Elements("metatype")
                .Single(candidate => candidate.Element("name")!.Value == name);
            Assert.IsTrue(CharacterCreationMetatypeCatalogProjector.TryProjectPriorityRacialOption(
                row, ["SR5"], out var option), name);
            Assert.IsTrue(option.IsEnabled, name);
            Assert.IsNull(option.BaseBonuses, name);
        }
    }

    /// <summary>
    /// Reproduces the pre-Elf/Ork Human-only catalog (every standard racial
    /// row disabled with the unsupported blocker). Used by bind-rejection
    /// tests that exercise the upgrade path from a disabled row.
    /// </summary>
    internal static CharacterCreationPrerequisiteAuthority HumanOnlyLegacyAuthority(CharacterCreationPrerequisiteAuthority current)
    {
        var baseline = current with { Options = current.Options.Select(row => row with
        { HeritageOptions = row.HeritageOptions.Select(option => option.RacialQualitySources is null ? option
            : option with { RacialQualitySources = null, BaseBonuses = null, IsEnabled = false,
                Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported] }).ToArray() }).ToArray() };
        return baseline with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(baseline) };
    }

    internal static CharacterCreationPrerequisiteAuthority LegacyAuthority(CharacterCreationPrerequisiteAuthority current)
    {
        // Reproduce the exact 80584eff baseline, not a relaxed validator:
        // Elf/Ork stay enabled WITH racial sources, only the newly admitted
        // standard Dwarf rows are downgraded, and no row carries BaseBonuses
        // (the field post-dates that baseline).
        var baseline = current with { Options = current.Options.Select(row => row with
        { HeritageOptions = row.HeritageOptions.Select(option =>
            !option.IsEnabled || option.MetavariantSourceId is not null
                || (option.MetatypeName != "Elf" && option.MetatypeName != "Ork"
                    && option.MetatypeName != "Dwarf")
                ? option
                : option.MetatypeName == "Dwarf"
                    ? option with { RacialQualitySources = null, BaseBonuses = null, IsEnabled = false,
                        Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported] }
                    : option with { BaseBonuses = null }).ToArray() }).ToArray() };
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
    [TestMethod]
    [DataRow("Human", CharacterCreationBuildMethods.Priority)]
    [DataRow("Human", CharacterCreationBuildMethods.SumToTen)]
    [DataRow("Elf", CharacterCreationBuildMethods.Priority)]
    [DataRow("Elf", CharacterCreationBuildMethods.SumToTen)]
    [DataRow("Ork", CharacterCreationBuildMethods.Priority)]
    [DataRow("Ork", CharacterCreationBuildMethods.SumToTen)]
    public void Baseline_authority_drafts_save_coldload_review_finalize(string metatype, string method)
    {
        ICharacterSourceDataResolver currentResolver = null!;
        using var context = ReadyContext.Create(true, buildMethod: method, metatypeName: metatype, wrapResolver: inner =>
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
        var draft = original.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!;
        Assert.AreEqual(metatype, draft.HeritageSelection!.MetatypeName);
        var sourceContext = currentResolver.TryCreateContext(reloaded.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        Assert.AreNotEqual(draft.AuthorityDigest, authority.AuthorityDigest,
            "the current authority must differ from the persisted baseline digest for this to exercise legacy compatibility");
        Assert.IsTrue(sourceContext.TryResolveCreationDefaultStartingNuyen(out var cash));
        var review = finalizer.Review(new(state.Binding)
            { StartingCash = new(cash!.AuthorityDigest, cash.Dice) }).Value!;
        Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
        var command = new CharacterCreationFinalizationConfirmRequest(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, "historical-" + metatype.ToLowerInvariant() + "-finalize", true)
            { StartingCash = review.Plan.StartingCash };
        var applied = finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
        var coldStore = new FileWorkspaceStore(context.Directory);
        var coldFinalizer = ReadyContext.BuildFinalizer(coldStore, context.Queries, currentResolver);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, coldFinalizer.Confirm(command).Outcome);
        Assert.AreEqual(metatype, XElement.Parse(coldStore.Get(context.WorkspaceId).Value!.Document.Content).Element("metatype")!.Value);
    }

    [TestMethod]
    public void Legacy_eligibility_binds_to_matched_reconstruction_and_80584_oracle()
    {
        // C3 gen3: the legacy compatibility gate must bind the DRAFT'S
        // SELECTED heritage to the MATCHED historical reconstruction, and
        // the production reconstruction must be provable against an
        // independently written exact-80584 oracle.
        using var context = ReadyContext.Create(true, metatypeName: "Ork");
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        var baseline = CharacterCreationPriorityRacialTests.LegacyAuthority(authority);
        var humanOnly = CharacterCreationPriorityRacialTests.HumanOnlyLegacyAuthority(authority);
        var draft = workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!;
        Assert.AreEqual("Ork", draft.HeritageSelection!.MetatypeName);

        // The pending-draft gate admits an Ork draft persisted under the
        // 80584eff baseline era (Ork was enabled there): re-stamp the draft's
        // authority digest to the baseline and recompute its draft digest.
        var orkLegacy = draft with { AuthorityDigest = baseline.AuthorityDigest };
        orkLegacy = orkLegacy with
        {
            DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(orkLegacy),
        };
        Assert.IsTrue(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyPendingDraftAuthority(orkLegacy, authority),
            "An Ork draft from the 80584eff era must keep its legacy eligibility.");

        // Counterexample 1: a Dwarf draft (newly admitted today) wearing the
        // 80584eff baseline digest must be REFUSED - Dwarf was disabled there.
        var dwarfSelection = draft.HeritageSelection with
        {
            SelectionId = FindDisabledDwarfSelectionId(authority),
            MetatypeName = "Dwarf",
            MetatypeSourceId = FindDisabledDwarfMetatypeSourceId(authority),
        };
        var dwarfDraft = draft with
        {
            AuthorityDigest = baseline.AuthorityDigest,
            HeritageSelection = dwarfSelection,
        };
        dwarfDraft = dwarfDraft with
        {
            DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(dwarfDraft),
        };
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyPendingDraftAuthority(dwarfDraft, authority),
            "A Dwarf draft must not bind to the Dwarf-disabled 80584eff history.");

        // Counterexample 2: the same Dwarf draft wearing the Human-only
        // digest must also be refused.
        var dwarfHuman = dwarfDraft with { AuthorityDigest = humanOnly.AuthorityDigest };
        dwarfHuman = dwarfHuman with
        {
            DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(dwarfHuman),
        };
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyPendingDraftAuthority(dwarfHuman, authority),
            "A Dwarf draft must not bind to the Human-only history either.");

        // Counterexample 3: an Elf draft (enabled at 80584eff) wearing the
        // Human-only digest must be REFUSED - Elf was not admitted there.
        var elfSelection = draft.HeritageSelection with
        {
            SelectionId = FindEnabledRacialSelectionId(authority, "Elf"),
            MetatypeName = "Elf",
            MetatypeSourceId = FindEnabledRacialMetatypeSourceId(authority, "Elf"),
        };
        var elfHuman = draft with
        {
            AuthorityDigest = humanOnly.AuthorityDigest,
            HeritageSelection = elfSelection,
        };
        elfHuman = elfHuman with
        {
            DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(elfHuman),
        };
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyPendingDraftAuthority(elfHuman, authority),
            "An Elf draft must not bind to the Human-only history.");

        // Independent 80584 oracle: the production reconstruction must be
        // byte-equal to this test's own independently derived oracle.
        Assert.IsTrue(CharacterCreationPriorityAuthorityCompatibility
            .TryReconstructLegacyElfOrkEnabledBaseline(baseline.AuthorityDigest, authority, out var produced));
        Assert.IsNotNull(produced);
        string oracleDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(
            Oracle80584Baseline(authority));
        Assert.AreEqual(oracleDigest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(produced),
            "Production reconstruction must equal the independent 80584 oracle.");
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .TryReconstructLegacyElfOrkEnabledBaseline(authority.AuthorityDigest, authority, out _),
            "The current extended authority must not reconstruct the 80584 baseline.");
    }

    /// <summary>Independent exact-80584 oracle written from the era's
    /// recorded facts (NOT by calling the production reconstructor):
    /// Elf/Ork enabled WITH racial sources, every standard Dwarf row
    /// disabled with its racial sources and base bonuses stripped (the
    /// BaseBonuses field post-dates that baseline), all other rows
    /// untouched.</summary>
    private static CharacterCreationPrerequisiteAuthority Oracle80584Baseline(
        CharacterCreationPrerequisiteAuthority current)
    {
        var options = current.Options
            .Select(row => row with
            {
                HeritageOptions = row.HeritageOptions
                    .Select(option =>
                        option.MetatypeName == "Dwarf"
                            && option.MetavariantSourceId is null
                            && option.IsEnabled
                            ? option with
                            {
                                IsEnabled = false,
                                RacialQualitySources = null,
                                BaseBonuses = null,
                                Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported],
                            }
                            : option)
                    .ToArray()
            })
            .ToArray();
        var oracle = current with { Options = options };
        return oracle with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(oracle) };
    }

    private static string FindDisabledDwarfSelectionId(
        CharacterCreationPrerequisiteAuthority authority)
    {
        var row = authority.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.MetatypeName == "Dwarf" && item.MetavariantSourceId is null);
        return row.SelectionId;
    }

    private static string FindDisabledDwarfMetatypeSourceId(
        CharacterCreationPrerequisiteAuthority authority)
    {
        var row = authority.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.MetatypeName == "Dwarf" && item.MetavariantSourceId is null);
        return row.MetatypeSourceId;
    }

    private static string FindEnabledRacialSelectionId(
        CharacterCreationPrerequisiteAuthority authority, string metatype)
    {
        var row = authority.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.MetatypeName == metatype && item.MetavariantSourceId is null && item.IsEnabled);
        return row.SelectionId;
    }

    private static string FindEnabledRacialMetatypeSourceId(
        CharacterCreationPrerequisiteAuthority authority, string metatype)
    {
        var row = authority.Options.SelectMany(item => item.HeritageOptions)
            .First(item => item.MetatypeName == metatype && item.MetavariantSourceId is null && item.IsEnabled);
        return row.MetatypeSourceId;
    }
    [TestMethod]
    public void Compatibility_recognizers_match_only_exact_baseline_reconstructions()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Ork");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        var baseline = CharacterCreationPriorityRacialTests.LegacyAuthority(authority);
        var humanOnly = CharacterCreationPriorityRacialTests.HumanOnlyLegacyAuthority(authority);

        // The 80584eff baseline (Elf/Ork enabled) matches ONLY the
        // Dwarf-only downgrade reconstruction; the Human-only
        // reconstruction (which also disables Elf/Ork) must NOT match.
        Assert.IsTrue(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyElfOrkEnabledBaseline(baseline.AuthorityDigest, authority));
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyHumanCatalog(baseline.AuthorityDigest, authority));
        // The Human-only catalog matches ONLY the full downgrade.
        Assert.IsTrue(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyHumanCatalog(humanOnly.AuthorityDigest, authority));
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyElfOrkEnabledBaseline(humanOnly.AuthorityDigest, authority));
        // The current extended authority matches neither reconstruction.
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyElfOrkEnabledBaseline(authority.AuthorityDigest, authority));
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyHumanCatalog(authority.AuthorityDigest, authority));
        // Tampered authorities fail closed.
        var tampered = authority with
        {
            RawProfileInputsDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different profile")
        };
        var rehashed = tampered with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(tampered) };
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyElfOrkEnabledBaseline(baseline.AuthorityDigest, rehashed));
        Assert.IsFalse(CharacterCreationPriorityAuthorityCompatibility
            .MatchesLegacyHumanCatalog(baseline.AuthorityDigest, rehashed));
    }
}
