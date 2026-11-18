using System.Text.Json;
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
public sealed class CharacterCreationDwarfPriorityTests
{
    private static IReadOnlyDictionary<string, string> RanksWithHeritageC =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "C",
            [CharacterCreationPriorityCategoryIds.Talent] = "E",
            [CharacterCreationPriorityCategoryIds.Attributes] = "B",
            [CharacterCreationPriorityCategoryIds.Skills] = "D",
            [CharacterCreationPriorityCategoryIds.Resources] = "A"
        };

    [TestMethod]
    public void Dwarf_priority_C_is_enabled_with_one_special_point_and_zero_karma()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Dwarf",
            rankAssignments: RanksWithHeritageC);
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));

        var rankCandidates = authority.Options.Where(
            option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                      && option.Rank == "C").ToArray();
        Assert.AreEqual(1, rankCandidates.Length,
            string.Join(" | ", rankCandidates.Select(item => item.SourceId)));
        var rankC = rankCandidates[0];
        var dwarves = rankC.HeritageOptions.Where(option => option.MetatypeName == "Dwarf").ToArray();
        Assert.AreEqual(1, dwarves.Count(option => option.IsEnabled),
            string.Join(" | ", dwarves.Select(item =>
                item.SelectionId + ":" + (item.MetavariantSourceId ?? "-") + ":" + item.IsEnabled)));
        var dwarf = dwarves.Single(option => option.IsEnabled);
        Assert.IsTrue(dwarf.IsEnabled, string.Join(",", dwarf.Blockers));
        Assert.AreEqual(1, dwarf.SpecialAttributePoints);
        Assert.AreEqual(0, dwarf.KarmaCost);
        Assert.IsFalse(dwarf.HalvesNormalAttributePoints);
        Assert.IsNotNull(dwarf.BaseBonuses, "Dwarf must carry its source-bound lifestyle surcharge.");
        Assert.AreEqual(0, dwarf.BaseBonuses.Armor);
        Assert.AreEqual(0, dwarf.BaseBonuses.Reach);
        Assert.AreEqual(20, dwarf.BaseBonuses.LifestyleCostPercent);
        Assert.AreEqual(2, dwarf.RacialQualitySources!.Count);
    }

    [TestMethod]
    public void Dwarf_is_unavailable_at_priority_D_and_E()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Human");
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        foreach (string rank in new[] { "D", "E" })
        {
            var rankRow = authority.Options.Single(
                option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                          && option.Rank == rank);
            Assert.IsTrue(rankRow.HeritageOptions.All(
                    option => option.MetatypeName != "Dwarf" || !option.IsEnabled),
                $"Dwarf must not be enabled at priority {rank}.");
        }
    }

    [TestMethod]
    public void Human_elf_ork_options_carry_no_base_bonuses_so_digests_stay_stable()
    {
        using var context = ReadyContext.Create(true, metatypeName: "Human");
        var workspace = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationPrerequisiteAuthority(out var authority));
        var heritageOptions = authority.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage)
            .SelectMany(option => option.HeritageOptions);
        foreach (var option in heritageOptions.Where(item =>
                     item.MetatypeName is "Human" or "Elf" or "Ork"))
        {
            Assert.IsNull(option.BaseBonuses,
                $"{option.MetatypeName} must not gain a base-bonus projection.");
        }
    }

    private static void AssertDwarfFinalization(string method)
    {
        using var context = ReadyContext.Create(true, buildMethod: method, metatypeName: "Dwarf",
            rankAssignments: RanksWithHeritageC);
        var store = context.Store;
        var finalizer = ReadyContext.BuildFinalizer(store, context.Queries, context.Resolver);
        var state = finalizer.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanReview, string.Join(",", state.Blockers));
        var workspace = store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationDefaultStartingNuyen(out var cash));
        var review = finalizer.Review(new(state.Binding)
            { StartingCash = new(cash!.AuthorityDigest, cash.Dice) }).Value!;
        Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
        var command = new CharacterCreationFinalizationConfirmRequest(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, "dwarf-priority-finalize", true) { StartingCash = review.Plan.StartingCash };
        var applied = finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome,
            string.Join(",", applied.Blockers));

        string finalXml = store.Get(context.WorkspaceId).Value!.Document.Content;
        var root = XElement.Parse(finalXml);
        Assert.AreEqual("Dwarf", root.Element("metatype")!.Value);

        // 20% lifestyle surcharge materialized through the Karma metatype-bonus grammar.
        var improvements = root.Element("improvements");
        Assert.IsNotNull(improvements, "Finalized document must carry an improvements container.");
        var lifestyle = improvements.Elements("improvement").Single(
            item => item.Element("improvementttype")!.Value == "LifestyleCost");
        Assert.AreEqual("20", lifestyle.Element("val")!.Value);
        Assert.AreEqual("Metatype", lifestyle.Element("improvementsource")!.Value);
        Assert.IsTrue(review.OrderedDeltas.Any(item =>
            item.DeltaId == "priority-metatype-bonus:LifestyleCost"));

        // Admitted heritage movement persists source-bound exactly once
        // (fix 3: the ordinary finalizer writes heritage.Movement via the
        // shared CharacterCreationHeritageGraph832.ApplyMovement materializer).
        var movement = root.Element("movement");
        Assert.IsNotNull(movement, "Finalized Dwarf must persist a movement element.");
        Assert.AreEqual(string.Empty, movement.Value);
        Assert.AreEqual("2/1/0", root.Element("walk")!.Value, "Dwarf walk must persist.");
        Assert.AreEqual("4/0/0", root.Element("run")!.Value, "Dwarf run must persist.");
        Assert.AreEqual("1/1/0", root.Element("sprint")!.Value, "Dwarf sprint must persist.");
        // Racial qualities and their literal resistance effects are retained.
        var qualities = root.Elements("qualities").Descendants("quality").ToList();
        Assert.IsTrue(qualities.Any(item => item.Element("name")!.Value == "Thermographic Vision"),
            "Dwarf's Thermographic Vision quality must be written.");
        Assert.IsTrue(qualities.Any(item =>
                item.Element("name")!.Value.StartsWith("Resistance to Pathogens", StringComparison.OrdinalIgnoreCase)
                || item.Element("name")!.Value.StartsWith("Resistance to Toxins", StringComparison.OrdinalIgnoreCase)
                || item.Element("name")!.Value.Contains("Pathogens")
                || item.Element("name")!.Value.Contains("Toxins")),
            "Dwarf's pathogen/toxin resistance quality must be written.");
        foreach (string resistanceType in new[]
                 {
                     "PathogenContactResist", "PathogenIngestionResist", "PathogenInhalationResist",
                     "PathogenInjectionResist", "ToxinContactResist", "ToxinIngestionResist",
                     "ToxinInhalationResist", "ToxinInjectionResist"
                 })
        {
            Assert.IsTrue(improvements.Elements("improvement").Any(
                    item => item.Element("improvementttype")!.Value == resistanceType),
                $"Resistance improvement {resistanceType} must be materialized.");
        }
    }

    [TestMethod]
    public void Racial_resistance_effects_are_admitted_for_completion_independence()
    {
        // C1 gen3: the lifestyles/contacts completion independence proof must
        // admit the exact shared literal resistance grammar for racial grants
        // (the Dwarf's source-bound qualities carry these effects). Unknown
        // or malformed racial effects still fail closed. The racial sources
        // come from the real seeded Dwarf Priority-C workspace so every
        // digest/identity field is genuine; only the bonus grammar is varied.
        using var context = ReadyContext.Create(true, metatypeName: "Dwarf",
            rankAssignments: RanksWithHeritageC);
        var prerequisiteService = new CharacterCreationPrerequisiteService(
            context.Store, context.Queries, context.Resolver);
        var state = prerequisiteService.Load(new(context.WorkspaceId)).Value!;
        var prerequisite = state.PendingDraft;
        Assert.IsNotNull(prerequisite, "The seeded Dwarf Priority-C workspace must carry a pending draft.");
        var heritage = prerequisite.HeritageSelection;
        Assert.IsNotNull(heritage, "Dwarf draft must carry a heritage selection.");
        Assert.IsNotNull(heritage.RacialQualitySources, "Dwarf draft must carry racial quality sources.");
        Assert.IsTrue(CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(
            prerequisite, []), "Dwarf racial resistance effects must be admitted.");

        // An unknown racial effect fails closed (grammar-varied source with
        // recomputed digests so validation reaches the effect switch).
        Assert.IsFalse(AreIndependentWithBonus(prerequisite, heritage,
            "<bonus><someunknownbonus>1</someunknownbonus></bonus>"),
            "Unknown racial effects must fail closed.");

        // A negative resistance value fails closed.
        Assert.IsFalse(AreIndependentWithBonus(prerequisite, heritage,
            "<bonus><toxincontactresist>-1</toxincontactresist></bonus>"),
            "Negative resistance values must fail closed.");

        // A well-formed resistance value is still admitted.
        Assert.IsTrue(AreIndependentWithBonus(prerequisite, heritage,
            "<bonus><toxincontactresist>2</toxincontactresist></bonus>"),
            "Well-formed resistance values must be admitted.");
    }

    /// <summary>Builds a draft whose single racial source carries the given
    /// bonus grammar grafted onto the REAL seeded Dwarf racial quality row
    /// (identity scalars stay genuine; digests recomputed) so validation
    /// passes and the independence proof reaches the racial effect switch.</summary>
    private static bool AreIndependentWithBonus(
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationPriorityHeritageSelection heritage,
        string bonusXml)
    {
        CharacterCreationTalentQualitySource real = heritage.RacialQualitySources!
            .FirstOrDefault(s => XElement.Parse(s.CanonicalSourceXml).Element("bonus") is not null)
            ?? heritage.RacialQualitySources[0];
        var row = XElement.Parse(real.CanonicalSourceXml);
        row.Element("bonus")!.Remove();
        row.Add(XElement.Parse(bonusXml));
        string canonical = row.ToString(SaveOptions.DisableFormatting);
        string inputsDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(canonical);
        var source = real with
        {
            CanonicalSourceXml = canonical,
            CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(canonical),
            EffectiveSourceDigest = inputsDigest,
            SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(
                inputsDigest, real.SourceId, canonical),
        };
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(source),
            "The grafted source must be valid so the effect switch is reached.");
        return CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(
            prerequisite with
            {
                HeritageSelection = heritage with { RacialQualitySources = [source] }
            },
            []);
    }

    [TestMethod]
    public void Dwarf_priority_C_finalization_writes_surcharge_and_resistances()
    {
        AssertDwarfFinalization(CharacterCreationBuildMethods.Priority);
    }

    [TestMethod]
    public void Dwarf_sum_to_ten_finalization_writes_surcharge_and_resistances()
    {
        AssertDwarfFinalization(CharacterCreationBuildMethods.SumToTen);
    }

    [TestMethod]
    public void Selected_graph_materializes_shared_metatype_base_bonus_surcharge()
    {
        // The selected/completion graph sites (CharacterCreationSelectedPrerequisiteConsumer
        // and CompileSelectedGraph) must materialize the Priority Dwarf heritage
        // base bonuses (20% LifestyleCost surcharge) through the SAME shared strict
        // source-bound materializer the ordinary finalization projector uses:
        // identical Metatype-owned improvement element and matching review delta,
        // so both execution paths cannot drift. The prerequisite draft comes from
        // the real Dwarf Priority-C seeded workspace via the prerequisite service;
        // the wire pair under test is exactly what both completion sites execute
        // after racial TryApply: CharacterCreationMetatypeBaseBonusMaterializer.TryApply
        // with the shared GetOrCreateDirectContainer semantics.
        using var context = ReadyContext.Create(true, metatypeName: "Dwarf",
            rankAssignments: RanksWithHeritageC);
        var prerequisiteService = new CharacterCreationPrerequisiteService(
            context.Store, context.Queries, context.Resolver);
        var state = prerequisiteService.Load(new(context.WorkspaceId)).Value!;
        var prerequisite = state.PendingDraft;
        Assert.IsNotNull(prerequisite, "The seeded Dwarf Priority-C workspace must carry a pending draft.");
        Assert.IsNotNull(prerequisite!.HeritageSelection, "Dwarf draft must carry a heritage selection.");
        Assert.IsNotNull(prerequisite.HeritageSelection!.BaseBonuses,
            "Dwarf selection must retain its source-bound base bonuses.");
        Assert.AreEqual(20, prerequisite.HeritageSelection.BaseBonuses!.LifestyleCostPercent);

        // The wired completion-site pair: racial TryApply, then the shared materializer
        // through the same GetOrCreateDirectContainer the ordinary projector delegates to.
        var root = new XElement("character", new XElement("qualities"), new XElement("gears"),
            new XElement("metatype", "Dwarf"));
        foreach (string flag in new[] { "magenabled", "resenabled", "depenabled" })
            root.Add(new XElement(flag, "False"));
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        Assert.IsTrue(CharacterCreationPriorityRacialQualities.TryApply(root, prerequisite.HeritageSelection,
            prerequisite.DraftDigest, deltas, ref order));
        Assert.IsTrue(CharacterCreationMetatypeBaseBonusMaterializer.TryApply(prerequisite.HeritageSelection,
            root, deltas, ref order));
        var improvements = root.Element("improvements");
        Assert.IsNotNull(improvements, "The shared materializer must create the improvements container.");
        var lifestyle = improvements.Elements("improvement").Single(
            item => item.Element("improvementttype")!.Value == "LifestyleCost");
        Assert.AreEqual("20", lifestyle.Element("val")!.Value);
        Assert.AreEqual("Metatype", lifestyle.Element("improvementsource")!.Value);
        Assert.AreEqual(prerequisite.HeritageSelection.MetatypeSourceId,
            lifestyle.Element("sourcename")!.Value);
        var delta = deltas.Single(item => item.DeltaId == "priority-metatype-bonus:LifestyleCost");
        Assert.AreEqual("20", delta.AfterValue);
        Assert.AreEqual("0", delta.BeforeValue);
        Assert.AreEqual(CharacterCreationFinalizationDeltaKinds.Metatype, delta.Kind);
        CollectionAssert.Contains(delta.SourceAnchorIds.ToArray(),
            "metatypes.xml#metatype:" + prerequisite.HeritageSelection.MetatypeSourceId + "/bonus/lifestylecost");
        // The Dwarf source binds only the lifestyle surcharge: no Armor/Reach deltas.
        Assert.IsFalse(deltas.Any(item => item.DeltaId.StartsWith("priority-metatype-bonus:", StringComparison.Ordinal)
                                          && item.DeltaId != "priority-metatype-bonus:LifestyleCost"));
    }
}