using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFinalizationServiceTests
{
    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Aspected Magician")]
    [DataRow("Mystic Adept")]
    [DataRow("Adept")]
    [DataRow("Technomancer")]
    public void Actual_awakened_priority_finishes_and_cold_reopens_without_losing_talent_effects(string talentValue)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, talentValue: talentValue);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var magic = before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var skills = before.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
        var qualityService = new CharacterCreationQualitiesService(context.Store, context.Resolver,
            new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver),
            new CharacterCreationAttributesService(context.Store, context.Resolver));
        var qualityState = qualityService.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(qualityState.CanEdit, string.Join(",", qualityState.Blockers));
        Assert.AreEqual(1, qualityState.Preview.GrantedQualities.Count);
        Assert.AreEqual(0, qualityState.Preview.PositiveQualityBudget.Used);
        Assert.IsFalse(qualityState.Preview.GrantedQualities.Single().CountsAgainstKarma);
        Assert.IsTrue(qualityState.Preview.GrantedQualities.Single().KarmaCost > 0);
        if (talentValue == "Magician") AssertAwakenedForgeryRejected(before);
        if (talentValue == "Technomancer") AssertTechnomancerForgeryRejected(context, before);
        var state = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, state.Outcome, string.Join(",", state.Blockers));
        var review = context.Finalizer.Review(new(state.Value!.Binding));
        Assert.IsTrue(review.Value!.CanConfirm, string.Join(",", review.Blockers));
        if (talentValue == "Technomancer")
        {
            string[] gearAnchors = magic.FinalizationContribution!.Talent.GrantedQualitySources!.Single()
                .GrantedGearSources!.Single().SourceAnchorIds.ToArray();
            CollectionAssert.IsSubsetOf(gearAnchors, magic.SourceAnchorIds.ToArray());
            CollectionAssert.IsSubsetOf(gearAnchors, magic.FinalizationContribution.SourceAnchorIds.ToArray());
            CollectionAssert.IsSubsetOf(gearAnchors, review.Value.Plan!.SourceAnchorIds.ToArray());
        }
        var command = new CharacterCreationFinalizationConfirmRequest(state.Value.Binding,
            review.Value.PreviewDigest, review.Value.Plan!.PlanDigest, "actual-awakened-finalization", true);
        Assert.AreNotEqual(CharacterCreationFinalizationOutcomes.Applied,
            context.Finalizer.Confirm(command with { ExplicitlyConfirmed = false }).Outcome);
        Assert.AreEqual(before.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        var confirmed = context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        using ReadyContext cold = context.Restart();
        var after = cold.Store.Get(context.WorkspaceId).Value!;
        XElement root = XElement.Parse(after.Document.Content);
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.AreEqual(talentValue == "Technomancer" ? "False" : "True", root.Element("magenabled")!.Value);
        Assert.AreEqual(talentValue == "Technomancer" ? "True" : "False", root.Element("resenabled")!.Value);
        Assert.AreEqual(talentValue is "Adept" or "Mystic Adept" ? "True" : "False", root.Element("adept")!.Value);
        Assert.AreEqual(talentValue is "Adept" or "Technomancer" ? "False" : "True", root.Element("magician")!.Value);
        XElement heritage = root.Element("qualities")!.Elements("quality").Single(item => item.Element("qualitysource")?.Value == "Heritage");
        Assert.AreEqual(talentValue, heritage.Element("name")!.Value);
        var source = magic.FinalizationContribution!.Talent.GrantedQualitySources!.Single();
        Assert.AreEqual(source.SourceId, heritage.Element("sourceid")!.Value);
        XElement[] improvements = root.Element("improvements")!.Elements("improvement").ToArray();
        Assert.IsTrue(improvements.Any(item => item.Element("improvementttype")?.Value == "Attribute"
            && item.Element("improvedname")?.Value == (talentValue == "Technomancer" ? "RES" : "MAG")
            && item.Element("unique")?.Value == "enableattribute"));
        if (talentValue == "Technomancer")
        {
            XElement persona = root.Element("gears")!.Elements("gear").Single(item => item.Element("name")?.Value == "Living Persona");
            Assert.AreEqual("73b55822-dfb8-48f5-8ff8-37ef498ab9ef", persona.Element("sourceid")!.Value);
            Assert.AreEqual(heritage.Element("guid")!.Value, persona.Element("parentid")!.Value);
            Assert.AreEqual("0", persona.Element("cost")!.Value);
            Assert.AreEqual("True", persona.Element("active")!.Value);
            Assert.AreEqual("Self", persona.Element("canformpersona")!.Value);
            foreach (var (field, value) in new[] { ("devicerating", "{RES}"), ("attack", "{CHA}"),
                         ("sleaze", "{INT}"), ("dataprocessing", "{LOG}"), ("firewall", "{WIL}") })
                Assert.AreEqual(value, persona.Element(field)!.Value);
            Assert.IsTrue(improvements.Any(item => item.Element("improvementttype")?.Value == "Gear"
                && item.Element("improvedname")?.Value == persona.Element("guid")!.Value
                && item.Element("sourcename")?.Value == heritage.Element("guid")!.Value));
            XElement perception = improvements.Single(item => item.Element("improvementttype")?.Value == "Skill"
                && item.Element("improvedname")?.Value == "Computer");
            Assert.AreEqual("2", perception.Element("val")!.Value);
            Assert.AreEqual("Matrix Perception", perception.Element("condition")!.Value);
            Assert.AreEqual("0", perception.Element("addtorating")!.Value);
            Assert.AreEqual(heritage.Element("guid")!.Value, perception.Element("sourcename")!.Value);
            Assert.AreEqual(magic.Selections.ComplexForms.Count, root.Element("complexforms")!.Elements("complexform").Count());
            Assert.AreEqual("RES", root.Element("tradition")!.Element("traditiontype")!.Value);
        }
        foreach (var grant in skills.Skills.Where(item => item.GrantedRating > 0))
        {
            XElement saved = root.Element("newskills")!.Element("skills")!.Elements("skill")
                .Single(item => item.Element("suid")?.Value == grant.SourceSkillId);
            Assert.AreEqual(grant.Rating - grant.GrantedRating, (int?)saved.Element("base"));
            Assert.IsTrue(improvements.Any(item => item.Element("improvementttype")?.Value == "SkillBase"
                && item.Element("improvementsource")?.Value == "Heritage"
                && item.Element("improvedname")?.Value == grant.Name && (int?)item.Element("val") == grant.GrantedRating));
        }
        foreach (var grant in skills.SkillGroups.Where(item => item.GrantedRating > 0))
        {
            XElement saved = root.Element("newskills")!.Element("groups")!.Elements("group")
                .Single(item => item.Element("name")?.Value == grant.Name);
            Assert.AreEqual(grant.Rating - grant.GrantedRating, (int?)saved.Element("base"));
            Assert.IsTrue(improvements.Any(item => item.Element("improvementttype")?.Value == "SkillGroupBase"
                && item.Element("improvedname")?.Value == grant.Name && (int?)item.Element("val") == grant.GrantedRating));
            if (talentValue == "Aspected Magician")
                Assert.IsTrue(improvements.Any(item => item.Element("improvementttype")?.Value == "SpecialSkills"
                    && item.Element("improvedname")?.Value == grant.Name));
        }
        Assert.AreEqual(magic.Selections.Spells.Count, root.Element("spells")!.Elements("spell").Count());
        Assert.AreEqual(magic.Selections.AdeptPowers.Count, root.Element("powers")!.Elements("power").Count());
        foreach (var power in magic.FinalizationContribution!.AdeptPowers)
        {
            XElement saved = root.Element("powers")!.Elements("power").Single(item => item.Element("sourceid")!.Value == power.Identity.SourceId);
            XElement definition = XElement.Parse(power.CanonicalSourceXml);
            Assert.AreEqual("False", saved.Element("discounted")!.Value);
            Assert.AreEqual(definition.Element("points")!.Value, saved.Element("pointsperlevel")!.Value);
            Assert.AreEqual((definition.Element("adeptwayrequires") ?? new XElement("adeptwayrequires")).ToString(SaveOptions.DisableFormatting),
                saved.Element("adeptwayrequires")!.ToString(SaveOptions.DisableFormatting));
        }
        if (magic.Selections.Tradition is not null)
        {
            XElement tradition = root.Element("tradition")!;
            Assert.AreEqual("Hermetic", tradition.Element("name")!.Value);
            Assert.AreEqual("{WIL} + {LOG}", tradition.Element("drain")!.Value);
            Assert.AreEqual("Spirit of Fire", tradition.Element("spiritcombat")!.Value);
        }
        Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidTransition(context.WorkspaceId,
            before.ContentRevision, before.SavedRevision, after.ContentRevision, before.Document, after.Document));
        Assert.AreEqual(confirmed.Value!.ReceiptDigest, cold.Finalizer.Confirm(command).Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, cold.Store.Get(context.WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(after.Document.Content, cold.Store.Get(context.WorkspaceId).Value!.Document.Content);
    }

    private static void AssertTechnomancerForgeryRejected(ReadyContext context, WorkspaceStoredDocument original)
    {
        var draft = original.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var contribution = draft.FinalizationContribution!;
        var quality = contribution.Talent.GrantedQualitySources!.Single();
        var source = quality.GrantedGearSources!.Single();
        var sourceContext = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(sourceContext.TryResolveCreationMagicResonanceAuthority(out var authority));
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(contribution, draft, authority));
        foreach (var (change, unsupported) in new (Action<XElement>, bool)[]
        {
            (node => node.Element("firewall")!.Value = "{CHA}", false),
            (node => node.Add(new XElement("bonus", new XElement("invented-effect"))), true),
            (node => node.Add(new XElement("hide", "hidden instructions")), true),
            (node => node.Add(new XText("uninterpreted source text")), true),
            (node => node.Add(new XElement("attack", "99")), true)
        })
        {
            XElement node = XElement.Parse(source.CanonicalSourceXml);
            change(node);
            string xml = node.ToString(SaveOptions.DisableFormatting);
            var forgedSource = source with { CanonicalSourceXml = xml,
                CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(xml),
                SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeGearNodeDigest(source.EffectiveSourceDigest, source.SourceId, xml) };
            var changedQuality = quality with { GrantedGearSources = [forgedSource] };
            var talent = contribution.Talent with { GrantedQualitySources = [changedQuality] };
            talent = talent with { ProjectionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeTalentProjectionDigest(talent) };
            var altered = contribution with { Talent = talent };
            altered = altered with { ContributionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(altered) };
            var forgedDraft = draft with { FinalizationContribution = altered };
            forgedDraft = forgedDraft with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(forgedDraft) };
            Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(altered, forgedDraft, authority),
                "Rehashing a nested gear source cannot replace independent current source authority.");
            if (unsupported)
            {
                var forgedDocument = original.Document with { State = original.Document.State with
                { AuxiliaryState = original.Document.AuxiliaryState with { CharacterCreationMagicResonanceDraft = forgedDraft } } };
                Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(original with { Document = forgedDocument },
                    out var output, out var deltas, out _, out _, out _, out _, out _));
                Assert.AreEqual(string.Empty, output);
                Assert.IsEmpty(deltas);
            }
        }
        Assert.AreEqual(original.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
    }

    private static void AssertAwakenedForgeryRejected(WorkspaceStoredDocument original)
    {
        var magic = original.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var contribution = magic.FinalizationContribution!;
        var source = contribution.Talent.GrantedQualitySources!.Single();
        foreach (Action<XElement> change in new Action<XElement>[]
        {
            node => node.Element("bonus")!.Add(new XElement("invented-effect")),
            node => node.Element("bonus")!.ReplaceWith(new XElement("bonus")),
            node => node.Element("bonus")!.Add(new XText("uncompiled instructions")),
            node => node.Element("bonus")!.Element("enableattribute")!.Element("name")!.Value = "DEP",
            node => node.Element("forbidden")!.Element("oneof")!.Add(new XElement("quality", "Magician")),
            node => node.Add(new XElement("firstlevelbonus", new XElement("ambidextrous")))
        })
        {
            XElement node = XElement.Parse(source.CanonicalSourceXml);
            change(node);
            string xml = node.ToString(SaveOptions.DisableFormatting);
            var forgedSource = source with { CanonicalSourceXml = xml,
                CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(xml),
                SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(source.EffectiveSourceDigest, source.SourceId, xml) };
            var forgedTalent = contribution.Talent with { GrantedQualitySources = [forgedSource] };
            forgedTalent = forgedTalent with { ProjectionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeTalentProjectionDigest(forgedTalent) };
            var forged = contribution with { Talent = forgedTalent };
            forged = forged with { ContributionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(forged) };
            var draft = magic with { FinalizationContribution = forged };
            draft = draft with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(draft) };
            var document = original.Document with { State = original.Document.State with
            { AuxiliaryState = original.Document.AuxiliaryState with { CharacterCreationMagicResonanceDraft = draft } } };
            Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(original with { Document = document },
                out var output, out var deltas, out _, out _, out _, out _, out _));
            Assert.AreEqual(string.Empty, output);
            Assert.IsEmpty(deltas);
        }
        var skills = original.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
        foreach (int grant in new[] { 0, 99 })
        {
            var changed = skills with { Skills = skills.Skills.Select(item => item.GrantedRating > 0 ? item with { GrantedRating = grant } : item).ToArray() };
            changed = changed with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(changed) };
            var document = original.Document with { State = original.Document.State with
            { AuxiliaryState = original.Document.AuxiliaryState with { CharacterCreationSkillsDraft = changed } } };
            Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(original with { Document = document },
                out _, out _, out _, out _, out _, out _, out _));
        }
    }

    [TestMethod]
    public void Finalization_keeps_confirmed_step_history_while_consuming_only_pending_drafts()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, includeNonEmptyPurchases: true);
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;
        WorkspaceDocumentAuxiliaryState prior = before.Document.AuxiliaryState;
        Assert.IsNotEmpty(prior.CharacterCreationSkillsReceipts!);
        Assert.IsNotEmpty(prior.CharacterCreationQualitiesReceipts!);
        Assert.IsNotEmpty(prior.CharacterCreationResourcesReceipts!);
        Assert.IsNotEmpty(prior.CharacterCreationGearReceipts!);

        var state = AssertAvailable(context.Finalizer.Load(new(context.WorkspaceId)));
        var review = AssertAvailable(context.Finalizer.Review(new(state.Binding)));
        var result = context.Finalizer.Confirm(new(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, "keep-confirmed-step-history", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome,
            string.Join(",", result.Blockers));
        using ReadyContext cold = context.Restart();
        WorkspaceStoredDocument after = cold.Store.Get(context.WorkspaceId).Value!;
        var actual = after.Document.AuxiliaryState;
        Assert.IsNotNull(actual.CharacterCreationFinalizationArchive);
        Assert.IsFalse(actual.IsEmpty);
        Assert.IsFalse(after.Document.Content.Contains(nameof(CharacterCreationFinalizationArchive), StringComparison.Ordinal),
            "Build history stays in workspace storage, never in the downloadable runner XML.");
        Assert.AreEqual(before.Document.AuxiliaryStateDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(actual.CharacterCreationFinalizationArchive.State),
            "Finishing Creation must not erase the user's confirmed step history.");
        Assert.IsNull(actual.CharacterCreationBootstrapBinding);
        Assert.IsNull(actual.CharacterCreationPrerequisiteDraft);
        Assert.IsNull(actual.CharacterCreationAttributesDraft);
        Assert.IsNull(actual.CharacterCreationSkillsDraft);
        Assert.IsNull(actual.CharacterCreationSkillsReceipts);
        Assert.IsNull(actual.CharacterCreationGearDraft);
        Assert.IsNull(actual.CharacterCreationGearReceipts);
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(result.Value!.ReceiptDigest,
            cold.Finalizer.LookupReceipt(new(context.WorkspaceId, "keep-confirmed-step-history")).Value!.ReceiptDigest);
        Assert.HasCount(4, context.ReplayChecks);
        foreach (var verifyReplay in context.ReplayChecks)
            verifyReplay(cold.Store);
        Assert.AreEqual(after.ContentRevision, cold.Store.Get(context.WorkspaceId).Value!.ContentRevision,
            "Recovering a confirmed Creation step in Career must never apply it again.");

        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidTransition(
            context.WorkspaceId, before.ContentRevision, before.SavedRevision, after.ContentRevision,
            before.Document, after.Document));
        foreach (var changed in new[]
        {
            new WorkspaceDocumentAuxiliaryState(CharacterCreationFinalizationReceipts: actual.CharacterCreationFinalizationReceipts),
            actual with { CharacterCreationFinalizationArchive = null },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationGearReceipts = null }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationGearReceipts = [] }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationResourcesReceipts = null }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationSkillsReceipts = null }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationQualitiesReceipts = null }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationFinalizationArchive = new(prior) }) },
            actual with { CharacterCreationFinalizationArchive = new(prior with { CharacterCreationFinalizationReceipts = actual.CharacterCreationFinalizationReceipts }) },
            actual with { CharacterCreationFinalizationArchive = new(null!) },
            actual with { CharacterCreationAttributesDraft = prior.CharacterCreationAttributesDraft },
            actual with { CharacterCreationBootstrapBinding = prior.CharacterCreationBootstrapBinding }
        })
        {
            Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidTransition(
                context.WorkspaceId, before.ContentRevision, before.SavedRevision, after.ContentRevision,
                before.Document, after.Document with { State = after.Document.State with { AuxiliaryState = changed } }),
                "The store boundary must reject dropped history or retained pending selections.");
            var mutation = ((IWorkspaceAuxiliaryStateAtomicCommitCapability)cold.Store)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(context.WorkspaceId,
                    after.ContentRevision, after.Document.AuxiliaryStateDigest,
                    after.Document with { State = after.Document.State with { AuxiliaryState = changed } });
            Assert.IsFalse(mutation.Success,
                "A later generic write must not strip, replace or reactivate completed Creation history.");
            var unchanged = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
            Assert.AreEqual(after.ContentRevision, unchanged.ContentRevision);
            Assert.AreEqual(after.Document.AuxiliaryStateDigest, unchanged.Document.AuxiliaryStateDigest);
        }
    }

    [TestMethod]
    public void Finalization_archive_is_optional_for_legacy_state_but_never_accepts_nesting()
    {
        var empty = WorkspaceDocumentAuxiliaryState.Empty;
        Assert.IsTrue(empty.IsEmpty);
        Assert.AreEqual("{\"CharacterCreationFoundationDraft\":null,\"IsEmpty\":true}",
            System.Text.Json.JsonSerializer.Serialize(empty),
            "Adding optional build history must not change any pre-existing empty-state bytes.");
        Assert.IsFalse((empty with { CharacterCreationFinalizationArchive = new(empty) }).IsEmpty);
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            new("unfinalized"), 1, new(empty), null));
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            new("nested"), 1, new(empty with { CharacterCreationFinalizationArchive = new(empty) }), []));
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            new("null-entry"), 1, new(empty), [null!]));
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(
            new("null-receipt"), 1, [new("", "", null!)]));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Actual_priority_creation_reopens_with_usable_reputation_and_preserves_finalization_receipts(
        bool includePurchases, bool olderDraft)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, includeNonEmptyPurchases: includePurchases,
            beforeDrafts: olderDraft ? root =>
            {
                foreach (string field in CareerBaselineFields) root.Elements(field).Remove();
            } : null);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var state = AssertAvailable(context.Finalizer.Load(new(context.WorkspaceId)));
        var review = AssertAvailable(context.Finalizer.Review(new(state.Binding)));
        Assert.AreEqual(original.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content,
            "Reading or previewing an older draft must not initialize persisted state.");
        var initialization = review.OrderedDeltas.Where(delta => delta.DeltaId.StartsWith("career-initialization:", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(olderDraft ? 7 : 0, initialization);
        foreach (var delta in initialization)
        {
            Assert.IsNull(delta.BeforeValue);
            Assert.AreEqual(0m, delta.KarmaCost);
            Assert.AreEqual(0m, delta.NuyenCost);
            Assert.AreEqual(CareerBaselineFields.Take(4).Contains(delta.TargetId) ? "0" : "", delta.AfterValue);
        }
        const string finalizeKey = "creation-to-local-reputation";
        var finalized = context.Finalizer.Confirm(new(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, finalizeKey, ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome,
            string.Join(",", finalized.Blockers));
        using ReadyContext reopened = context.Restart();
        var before = reopened.Store.Get(reopened.WorkspaceId).Value!;
        var reputation = new WorkspaceCharacterCareerReputationService(reopened.Store, reopened.Resolver);
        var read = reputation.Read(reopened.WorkspaceId);
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, read.Outcome,
            "Actual Bootstrap + confirmed drafts + finalization produced unusable Career input: " + read.Error);
        Assert.IsNotNull(read.Snapshot);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.StreetCred);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.Notoriety);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.PublicAwareness);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.BurntStreetCred);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.CareerKarma);
        var request = new CharacterCareerReputationRequest(reopened.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(1, null, null), "First local Career reputation decision");
        var preview = reputation.Preview(request);
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        Assert.IsNotNull(preview.Preview);
        Assert.AreEqual(before.Document.Content, reopened.Store.Get(reopened.WorkspaceId).Value!.Document.Content);
        var command = preview.Preview.Command with { ExplicitlyConfirmed = true };
        var applied = reputation.Commit(command);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, applied.Outcome, applied.Error);
        var after = reopened.Store.Get(reopened.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.HasCount(1, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.HasCount(1, after.Document.AuxiliaryState.CharacterCareerReputationReceipts!);
        Assert.IsNotNull(before.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.AreEqual(original.Document.AuxiliaryStateDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        var beforeXml = XDocument.Parse(before.Document.Content).Root!;
        var afterXml = XDocument.Parse(after.Document.Content).Root!;
        foreach (string field in new[] { "karma", "nuyen", "settings", "qualities", "gears", "improvements", "expenses" })
            Assert.IsTrue(XNode.DeepEquals(beforeXml.Element(field), afterXml.Element(field)),
                "Reputation changed another finalized domain: " + field);
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(context.Directory), reopened.Resolver);
        Assert.AreEqual(1, cold.Read(reopened.WorkspaceId).Snapshot!.Reputation.Inputs.StreetCred);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, cold.Commit(command).Outcome);
        var recoveredFinalization = reopened.Finalizer.LookupReceipt(new(reopened.WorkspaceId, finalizeKey));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recoveredFinalization.Outcome);
        Assert.AreEqual(finalized.Value!.ReceiptDigest, recoveredFinalization.Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, reopened.Store.Get(reopened.WorkspaceId).Value!.ContentRevision);

        // A real first After Run grant must also work on this created runner,
        // and earned Career Karma must not include leftover Creation Karma.
        var rewards = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(context.Directory));
        var rewardRead = rewards.Read(reopened.WorkspaceId);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, rewardRead.Outcome, rewardRead.Error);
        Assert.IsEmpty(rewardRead.Snapshot!.Expenses);
        var rewardPreview = rewards.Preview(new(reopened.WorkspaceId, Guid.NewGuid(), Guid.NewGuid(),
            8, 12500, new DateTime(2078, 9, 7, 18, 0, 0), "First locally recorded run"));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, rewardPreview.Outcome, rewardPreview.Error);
        Assert.AreEqual(review.Plan.KarmaRemaining + 8, rewardPreview.Preview!.KarmaAfter);
        Assert.AreEqual(review.Plan.NuyenRemaining + 12500, rewardPreview.Preview.NuyenAfter);
        var rewardCommand = rewardPreview.Preview.Command with { ExplicitlyConfirmed = true };
        var rewardCommit = rewards.Commit(rewardCommand);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewardCommit.Outcome, rewardCommit.Error);
        var afterRun = new FileWorkspaceStore(context.Directory).Get(reopened.WorkspaceId).Value!;
        Assert.AreEqual(original.Document.AuxiliaryStateDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(afterRun.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        Assert.AreEqual(after.ContentRevision + 1, afterRun.ContentRevision);
        Assert.AreEqual(8, cold.Read(reopened.WorkspaceId).Snapshot!.Reputation.Inputs.CareerKarma);
        Assert.AreEqual(1, cold.Read(reopened.WorkspaceId).Snapshot!.Reputation.Inputs.StreetCred);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, cold.Commit(command).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, rewards.Commit(rewardCommand).Outcome);
        Assert.AreEqual(finalized.Value.ReceiptDigest,
            reopened.Finalizer.LookupReceipt(new(reopened.WorkspaceId, finalizeKey)).Value!.ReceiptDigest);
        Assert.AreEqual(afterRun.ContentRevision, reopened.Store.Get(reopened.WorkspaceId).Value!.ContentRevision);
    }

    private static readonly string[] CareerBaselineFields =
        ["streetcred", "notoriety", "publicawareness", "burntstreetcred", "expenses", "improvements", "contacts"];

    [TestMethod]
    public void Malformed_persisted_career_value_blocks_review_and_confirm_without_writing()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true,
            beforeDrafts: root => root.SetElementValue("streetcred", "invalid"));
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, loaded.Outcome);
        Assert.IsNotNull(loaded.Value);
        Assert.IsFalse(loaded.Value.CanReview);
        CollectionAssert.Contains(loaded.Blockers.ToList(), CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        var review = context.Finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value);
        Assert.IsFalse(review.Value.CanConfirm);
        Assert.IsNull(review.Value.Plan);
        var confirmed = context.Finalizer.Confirm(new(loaded.Value.Binding, review.Value.PreviewDigest,
            CharacterCreationFinalizationDigest.ComputeUtf8("no-valid-career-plan"),
            "malformed-career-must-not-finalize", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, confirmed.Outcome);
        Assert.IsNull(confirmed.Value);
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
    }

    [TestMethod]
    public void Finalization_preserves_present_manual_reputation_and_career_history()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, beforeDrafts: root =>
        {
            foreach ((string field, int value) in new[]
                     { ("streetcred", 3), ("notoriety", 4), ("publicawareness", 5), ("burntstreetcred", 2) })
                root.SetElementValue(field, value);
            root.Element("expenses")!.Add(XElement.Parse(
                "<expense><guid>11111111-1111-4111-8111-111111111111</guid><date>2078-09-07T18:00:00</date>"
                + "<type>Karma</type><amount>20</amount><refund>False</refund><forcecareervisible>False</forcecareervisible></expense>"));
            root.Element("contacts")!.Add(XElement.Parse("<contact><name>Existing contact</name></contact>"));
        });
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var state = AssertAvailable(context.Finalizer.Load(new(context.WorkspaceId)));
        var review = AssertAvailable(context.Finalizer.Review(new(state.Binding)));
        var result = context.Finalizer.Confirm(new(state.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, "preserve-career-inputs", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome, string.Join(",", result.Blockers));
        var after = context.Store.Get(context.WorkspaceId).Value!;
        var beforeRoot = XDocument.Parse(before.Document.Content).Root!;
        var afterRoot = XDocument.Parse(after.Document.Content).Root!;
        foreach (string field in CareerBaselineFields)
            Assert.IsTrue(XNode.DeepEquals(beforeRoot.Element(field), afterRoot.Element(field)), field);
        var read = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(context.Directory), context.Resolver)
            .Read(context.WorkspaceId);
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, read.Outcome, read.Error);
        Assert.AreEqual(20, read.Snapshot!.Reputation.Inputs.CareerKarma);
        Assert.AreEqual(0, read.Snapshot.Reputation.Inputs.NotorietyImprovement);
        Assert.AreEqual(3, read.Snapshot.Reputation.Inputs.StreetCred);
        Assert.AreEqual(2, read.Snapshot.Reputation.Inputs.BurntStreetCred);
    }

    [TestMethod]
    public void Projection_preserves_existing_effect_rows_when_adding_confirmed_quality_effects()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, includeNonEmptyPurchases: true);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var root = XDocument.Parse(saved.Document.Content).Root!;
        var existing = XElement.Parse("<improvement><improvementttype>Notoriety</improvementttype><val>2</val><enabled>1</enabled></improvement>");
        root.Element("improvements")!.Add(existing);
        // Projector preservation only. An imported nonempty effect graph before
        // attribute allocation is deliberately rejected by the Creation service;
        // this test must not claim that unsupported full-service route works.
        var projectedInput = saved with { Document = WithCharacterXml(saved.Document, root) };
        Assert.IsTrue(CharacterCreationFinalizationProjector.TryProject(projectedInput,
            out string xml, out _, out _, out _, out _, out _, out var blockers), string.Join(",", blockers));
        var output = XDocument.Parse(xml).Root!.Element("improvements")!;
        Assert.HasCount(1, output.Elements().Where(row => XNode.DeepEquals(existing, row)).ToArray());
        Assert.AreEqual(saved.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
    }

    [TestMethod]
    public void Malformed_present_career_inputs_fail_projection_without_repair_or_output()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var stored = context.Store.Get(context.WorkspaceId).Value!;
        string[] invalidNodes =
        [
            "<streetcred />", "<streetcred> 0</streetcred>", "<streetcred>1.5</streetcred>",
            "<streetcred><value>0</value></streetcred>", "<streetcred extra='0'>0</streetcred>",
            "<streetcred>2147483648</streetcred>", "<burntstreetcred>-1</burntstreetcred>",
            "<expenses>lost history</expenses>", "<expenses><wrong /></expenses>",
            "<expenses><expense><type>Karma</type><amount>bad</amount></expense></expenses>",
            "<expenses><expense><type>Karma</type><amount>2147483648</amount></expense></expenses>",
            "<improvements><improvement><improvementttype>Notoriety</improvementttype><val>bad</val></improvement></improvements>",
            "<contacts>lost contact</contacts>", "<contacts><wrong /></contacts>"
        ];
        foreach (string invalid in invalidNodes)
        {
            XElement root = XDocument.Parse(stored.Document.Content).Root!;
            XElement replacement = XElement.Parse(invalid);
            root.Element(replacement.Name)!.ReplaceWith(replacement);
            AssertInvalidCareerProjection(stored, root, invalid);
        }
        foreach (string field in CareerBaselineFields)
        {
            XElement root = XDocument.Parse(stored.Document.Content).Root!;
            root.Add(new XElement(root.Element(field)!));
            AssertInvalidCareerProjection(stored, root, "duplicate " + field);
            root = XDocument.Parse(stored.Document.Content).Root!;
            root.Element(field)!.Name = XName.Get(field, "urn:foreign");
            AssertInvalidCareerProjection(stored, root, "foreign namespace " + field);
        }
        Assert.AreEqual(stored.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        Assert.AreEqual(stored.ContentRevision, context.Store.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    private static void AssertInvalidCareerProjection(WorkspaceStoredDocument stored, XElement root, string reason)
    {
        var changed = stored with { Document = WithCharacterXml(stored.Document, root) };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(changed,
            out string xml, out var deltas, out var anchors, out _, out _, out _, out var blockers), reason);
        Assert.AreEqual("", xml, reason);
        Assert.IsEmpty(deltas, reason);
        Assert.IsEmpty(anchors, reason);
        CollectionAssert.Contains(blockers, CharacterCreationFinalizationBlockers.DraftAuthorityInvalid, reason);
    }

    private static WorkspaceDocument WithCharacterXml(WorkspaceDocument document, XElement root)
        => document with { State = document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } };

    [TestMethod]
    [DataRow("Human")]
    [DataRow("Elf")]
    public void Priority_projection_cannot_discard_a_life_module_foundation(string metatype)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationFinalizationProjector.TryProject(
            original, out _, out _, out _, out _, out _, out _, out _));
        WorkspaceStoredDocument mixed = WithPendingFoundation(original, metatype);
        string beforeDigest = mixed.Document.AuxiliaryStateDigest;

        bool projected = CharacterCreationFinalizationProjector.TryProject(
            mixed, out string xml, out var deltas, out var anchors,
            out _, out _, out _, out string[] blockers);

        Assert.IsFalse(projected,
            "A stale cross-method draft is not permission to discard or grant Life Module effects.");
        CollectionAssert.Contains(blockers, "creation-finalization-foundation-draft-not-applicable");
        Assert.AreEqual(string.Empty, xml);
        Assert.IsEmpty(deltas);
        Assert.IsEmpty(anchors);
        Assert.AreEqual(beforeDigest, mixed.Document.AuxiliaryStateDigest);
        Assert.AreEqual(original.ContentRevision, context.Store.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Cross_method_foundation_blocks_review_and_confirmation_without_a_write()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;
        var observedStore = new CommitThenReportUnavailableStore(context.Store)
        {
            ReadTransform = workspace => WithPendingFoundation(workspace, "Human")
        };
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            observedStore, context.Queries, context.Resolver);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> loaded =
            finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, loaded.Outcome);
        Assert.IsNotNull(loaded.Value);
        Assert.IsFalse(loaded.Value.CanReview);
        CollectionAssert.Contains(loaded.Blockers.ToList(),
            "creation-finalization-foundation-draft-not-applicable");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> review =
            finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value);
        Assert.IsFalse(review.Value.CanConfirm);
        Assert.IsNull(review.Value.Plan);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> confirmation =
            finalizer.Confirm(new(
                loaded.Value.Binding,
                review.Value.PreviewDigest,
                CharacterCreationFinalizationDigest.ComputeUtf8("no-projectable-plan"),
                "cross-method-foundation-must-not-finalize",
                ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, confirmation.Outcome);
        Assert.IsNull(confirmation.Value);
        Assert.AreEqual(0, observedStore.AtomicCommitCount);
        WorkspaceStoredDocument after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }

    private static WorkspaceStoredDocument WithPendingFoundation(
        WorkspaceStoredDocument workspace, string metatype)
    {
        // This is deliberately a stale/mixed-method input, not a claim that the
        // Foundation service lets a normal Priority user confirm a Life Module.
        var draft = new CharacterCreationFoundationDraftLedger(
            CharacterCreationFoundationSchemas.DraftLedgerV1,
            workspace.Id,
            DraftRevision: 1,
            BaseContentRevision: workspace.ContentRevision - 1,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(workspace.Document.Content),
            CharacterCreationFinalizationDigest.ComputeUtf8("life-module-source-test"),
            metatype,
            new CharacterCreationFoundationSelection("nationality-test", null),
            [], [], new Dictionary<string, string>(), ["source:life-module-test"],
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        draft = draft with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft) };
        return workspace with
        {
            Document = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = workspace.Document.AuxiliaryState with
                    {
                        CharacterCreationFoundationDraft = draft
                    }
                }
            }
        };
    }

    [TestMethod]
    public void Priority_projection_cannot_discard_accepted_life_module_history()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument mixed = PersistAcceptedLifeModuleHistoryWithoutFoundation(context);
        IReadOnlyList<LifeModuleDecisionAcceptance> history = mixed.Document.AuxiliaryState
            .LifeModuleDecisionAcceptances!;
        Assert.IsTrue(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
            mixed.Id,
            mixed.ContentRevision,
            history),
            "The regression input must be valid accepted history, not malformed foreign data.");

        bool projected = CharacterCreationFinalizationProjector.TryProject(
            mixed, out string xml, out var deltas, out var anchors,
            out _, out _, out _, out string[] blockers);

        Assert.IsFalse(projected);
        CollectionAssert.Contains(blockers,
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(string.Empty, xml);
        Assert.IsEmpty(deltas);
        Assert.IsEmpty(anchors);
    }

    [TestMethod]
    public void Accepted_life_module_history_blocks_review_and_confirmation_without_a_write()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument before = PersistAcceptedLifeModuleHistoryWithoutFoundation(context);
        var observedStore = new CommitThenReportUnavailableStore(context.Store);
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            observedStore, context.Queries, context.Resolver);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> loaded =
            finalizer.Load(new(before.Id));
        Assert.IsNotNull(loaded.Value);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> review =
            finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> confirmation =
            finalizer.Confirm(new(
                loaded.Value.Binding,
                review.Value.PreviewDigest,
                review.Value.Plan?.PlanDigest
                ?? CharacterCreationFinalizationDigest.ComputeUtf8("no-projectable-plan"),
                "accepted-life-module-history-must-not-finalize",
                ExplicitlyConfirmed: true));

        WorkspaceStoredDocument after = context.Store.Get(before.Id).Value!;
        Assert.IsNotNull(after.Document.AuxiliaryState.LifeModuleDecisionAcceptances,
            "Priority finalization must not erase accepted Life Module history.");
        Assert.HasCount(1, after.Document.AuxiliaryState.LifeModuleDecisionAcceptances);
        Assert.AreEqual(0, observedStore.AtomicCommitCount);
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, loaded.Outcome);
        Assert.IsFalse(loaded.Value.CanReview);
        CollectionAssert.Contains(loaded.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, review.Outcome);
        Assert.IsFalse(review.Value.CanConfirm);
        Assert.IsNull(review.Value.Plan);
        CollectionAssert.Contains(review.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, confirmation.Outcome);
        Assert.IsNull(confirmation.Value);
        CollectionAssert.Contains(confirmation.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
    }

    private static WorkspaceStoredDocument PersistAcceptedLifeModuleHistoryWithoutFoundation(
        ReadyContext context)
    {
        WorkspaceStoredDocument current = context.Store.Get(context.WorkspaceId).Value!;
        CharacterCreationFoundationDraftLedger foundation = PendingFoundationForTransition(current);
        LifeModuleDecisionAcceptance acceptance = AcceptedLifeModuleHistory(
            current.Id,
            current.ContentRevision);
        WorkspaceDocument withHistory = current.Document with
        {
            State = current.Document.State with
            {
                AuxiliaryState = current.Document.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = foundation,
                    LifeModuleDecisionAcceptances = [acceptance]
                }
            }
        };
        WorkspaceStoreMutationResult accepted = context.Store
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                current.Id,
                current.ContentRevision,
                current.Document.AuxiliaryStateDigest,
                withHistory);
        Assert.IsTrue(accepted.Success, accepted.Error);

        WorkspaceStoredDocument persisted = context.Store.Get(current.Id).Value!;
        WorkspaceDocument withoutFoundation = persisted.Document with
        {
            State = persisted.Document.State with
            {
                AuxiliaryState = persisted.Document.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = null
                }
            }
        };
        WorkspaceStoreMutationResult cleared = context.Store
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                persisted.Id,
                persisted.ContentRevision,
                persisted.Document.AuxiliaryStateDigest,
                withoutFoundation);
        Assert.IsTrue(cleared.Success, cleared.Error);

        WorkspaceStoredDocument result = context.Store.Get(current.Id).Value!;
        Assert.IsNull(result.Document.AuxiliaryState.CharacterCreationFoundationDraft);
        return result;
    }

    private static CharacterCreationFoundationDraftLedger PendingFoundationForTransition(
        WorkspaceStoredDocument workspace)
    {
        var draft = new CharacterCreationFoundationDraftLedger(
            CharacterCreationFoundationSchemas.DraftLedgerV1,
            workspace.Id,
            DraftRevision: 1,
            BaseContentRevision: workspace.ContentRevision,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(
                workspace.Document.Content),
            CharacterCreationFinalizationDigest.ComputeUtf8("life-module-source-transition-test"),
            "Human",
            new CharacterCreationFoundationSelection("nationality-transition-test", null),
            [], [], new Dictionary<string, string>(), ["source:life-module-transition-test"],
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        return draft with
        {
            DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
        };
    }

    private static LifeModuleDecisionAcceptance AcceptedLifeModuleHistory(
        CharacterWorkspaceId workspaceId,
        long previousWorkspaceRevision)
    {
        const string decisionId = "accepted-life-module-decision";
        const string sourceAnchor = "lifemodules.xml#module:accepted-history-test";
        string Digest(string value) =>
            LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(value);
        var fact = new OriginCanonicalNarrativeFact(
            "accepted-life-module-fact",
            "accepted-life-module",
            "Accepted Life Module history.",
            decisionId,
            [sourceAnchor],
            string.Empty);
        fact = fact with
        {
            FactDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(
                fact with { FactDigest = string.Empty })
        };
        string contentDigest = Digest("accepted-life-module-content");
        string sourceDigest = Digest("accepted-life-module-source");
        string rulesDigest = Digest("accepted-life-module-rules");
        string runtimeDigest = Digest("accepted-life-module-runtime");
        string graphDigest = Digest("accepted-life-module-graph");
        string mechanicsDigest = Digest("accepted-life-module-mechanics");
        var terminal = new LifeModuleDecisionAuthorityStep(
            OriginDossierSchemas.DecisionAuthorityStepV1,
            RulesetDefaults.Sr5,
            workspaceId.Value,
            previousWorkspaceRevision + 1,
            "local-single-user",
            "runner-accepted-history",
            "Accepted History Runner",
            "en-US",
            "sr5-life-modules-foundation",
            "nationality-accepted",
            1,
            "turn-accepted-history",
            2,
            "Accepted Life Module history.",
            "Continue character creation.",
            [],
            [fact],
            [decisionId],
            Digest("accepted-life-module-previous-turn"),
            graphDigest,
            Digest("accepted-life-module-decision-step"),
            contentDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest,
            mechanicsDigest)
        {
            IsTerminal = true
        };
        var receipt = new LifeModuleAcceptedDecisionReceipt(
            OriginDossierSchemas.AcceptedDecisionReceiptV1,
            decisionId,
            "accepted-life-module-choice",
            Digest("accepted-life-module-command"),
            Digest("accepted-life-module-idempotency"),
            previousWorkspaceRevision,
            previousWorkspaceRevision + 1,
            Digest("accepted-life-module-previous-content"),
            contentDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest,
            Digest("accepted-life-module-previous-decision"),
            Digest("accepted-life-module-previous-mechanics"),
            graphDigest,
            mechanicsDigest,
            "Accepted Life Module history.",
            [fact],
            string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeReceiptDigest(receipt)
        };
        return new LifeModuleDecisionAcceptance(receipt, terminal);
    }

    [TestMethod]
    public void Missing_required_step_and_partial_composite_write_fail_closed()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: false);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> missing =
            context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, missing.Outcome);
        CollectionAssert.Contains(
            missing.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.GearDraftRequired);

        WorkspaceStoredDocument current = context.Store.Get(context.WorkspaceId).Value!;
        WorkspaceDocument partial = current.Document with
        {
            State = current.Document.State with
            {
                AuxiliaryState = current.Document.AuxiliaryState with
                {
                    CharacterCreationPrerequisiteDraft = null,
                    CharacterCreationAttributesDraft = null
                }
            }
        };
        WorkspaceStoreMutationResult rejected =
            ((IWorkspaceAuxiliaryStateAtomicCommitCapability)context.Store)
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                context.WorkspaceId,
                current.ContentRevision,
                current.Document.AuxiliaryStateDigest,
                partial);
        Assert.IsFalse(rejected.Success, "A partial whole-build clear must never commit.");
        WorkspaceStoredDocument unchanged = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(current.ContentRevision, unchanged.ContentRevision);
        Assert.IsNotNull(unchanged.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        Assert.IsNotNull(unchanged.Document.AuxiliaryState.CharacterCreationAttributesDraft);
    }

    [TestMethod]
    public void Finalization_is_digest_bound_idempotent_restart_recoverable_and_reopens_in_career()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        CharacterCreationFinalizationState state = AssertAvailable(
            context.Finalizer.Load(new(context.WorkspaceId)));
        Assert.IsTrue(state.CanReview);
        Assert.IsTrue(state.Steps.All(static step => step.IsComplete));

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> stale =
            context.Finalizer.Review(new(state.Binding with
            {
                ContentRevision = state.Binding.ContentRevision - 1
            }));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, stale.Outcome);
        CollectionAssert.Contains(
            stale.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.StaleWorkspaceRevision);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> staleDigest =
            context.Finalizer.Review(new(state.Binding with
            {
                RawCharacterXmlDigest = CharacterCreationFinalizationDigest.ComputeUtf8("stale")
            }));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, staleDigest.Outcome);
        CollectionAssert.Contains(
            staleDigest.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.StaleRawCharacterXmlDigest);

        CharacterCreationFinalizationReview review = AssertAvailable(
            context.Finalizer.Review(new(state.Binding)));
        Assert.IsTrue(review.CanConfirm);
        Assert.IsNotNull(review.Plan);
        Assert.IsTrue(review.OrderedDeltas.Count > 3);
        Assert.IsTrue(review.OrderedDeltas.Select(static delta => delta.Order)
            .SequenceEqual(Enumerable.Range(1, review.OrderedDeltas.Count)));

        const string idempotencyKey = "finalize-priority-mundane-test-0001";
        CharacterCreationFinalizationConfirmRequest command = new(
            state.Binding,
            review.PreviewDigest,
            review.Plan!.PlanDigest,
            idempotencyKey,
            ExplicitlyConfirmed: true);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> applied =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome,
            string.Join(",", applied.Blockers));
        CharacterCreationFinalizationReceipt receipt = applied.Value!;
        Assert.IsTrue(receipt.CharacterCreated);
        Assert.IsTrue(receipt.RequiresFreshCareerReopen);
        Assert.AreEqual(receipt.ContentRevision, receipt.SavedRevision);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> duplicate =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, duplicate.Outcome);
        Assert.AreEqual(receipt.ReceiptDigest, duplicate.Value!.ReceiptDigest);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> conflicting =
            context.Finalizer.Confirm(command with
            {
                PlanDigest = CharacterCreationFinalizationDigest.ComputeUtf8("different-plan")
            });
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, conflicting.Outcome);
        CollectionAssert.Contains(
            conflicting.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.IdempotencyConflict);

        ReadyContext restarted = context.Restart();
        using (restarted)
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> recovered =
                restarted.Finalizer.LookupReceipt(new(restarted.WorkspaceId, idempotencyKey));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome);
            Assert.AreEqual(receipt.ReceiptDigest, recovered.Value!.ReceiptDigest);

            WorkspaceStoredDocument reopened = restarted.Store.Get(restarted.WorkspaceId).Value!;
            CharacterFileSummary summary = restarted.Queries.ParseSummary(
                new CharacterDocument(reopened.Document.Content));
            Assert.IsTrue(summary.Created, "A fresh process must reopen the finalized runner in Career.");
            Assert.AreEqual(CharacterCreationBuildMethods.Priority, summary.BuildMethod);
            Assert.AreEqual(receipt.ContentRevision, reopened.ContentRevision);
            Assert.AreEqual(receipt.SavedRevision, reopened.SavedRevision);
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationGearDraft);
            Assert.HasCount(1,
                reopened.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        }
    }

    [TestMethod]
    public void Unknown_commit_outcome_recovers_the_exact_durable_receipt()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var ambiguousStore = new CommitThenReportUnavailableStore(context.Store);
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            ambiguousStore,
            context.Queries,
            context.Resolver);
        CharacterCreationFinalizationState state = AssertAvailable(finalizer.Load(new(context.WorkspaceId)));
        CharacterCreationFinalizationReview review = AssertAvailable(finalizer.Review(new(state.Binding)));
        const string idempotencyKey = "finalize-priority-unknown-outcome-0001";

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> recovered =
            finalizer.Confirm(new(
                state.Binding,
                review.PreviewDigest,
                review.Plan!.PlanDigest,
                idempotencyKey,
                ExplicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome);
        Assert.IsNotNull(recovered.Value);
        Assert.IsTrue(recovered.Value.CharacterCreated);
        Assert.AreEqual(1, ambiguousStore.AtomicCommitCount);
        CharacterFileSummary reopened = context.Queries.ParseSummary(new(
            context.Store.Get(context.WorkspaceId).Value!.Document.Content));
        Assert.IsTrue(reopened.Created);
    }

    [TestMethod]
    public void Nonempty_quality_and_gear_are_source_bound_atomically_finalized_and_reopened()
    {
        using ReadyContext context = ReadyContext.Create(
            includeGearReview: true,
            includeNonEmptyPurchases: true);
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;
        CharacterCreationQualitiesDraft qualityDraft = before.Document.AuxiliaryState
            .CharacterCreationQualitiesDraft!;
        CharacterCreationGearDraft gearDraft = before.Document.AuxiliaryState
            .CharacterCreationGearDraft!;
        Assert.HasCount(1, qualityDraft.Selections);
        Assert.HasCount(1, gearDraft.Lines);
        CharacterCreationQualitySelection selectedQuality = qualityDraft.Selections.Single();
        CharacterCreationGearLine selectedGear = gearDraft.Lines.Single();

        CharacterCreationFinalizationState state = AssertAvailable(
            context.Finalizer.Load(new(context.WorkspaceId)));
        CharacterCreationFinalizationReview review = AssertAvailable(
            context.Finalizer.Review(new(state.Binding)));
        Assert.IsTrue(review.OrderedDeltas.Any(delta =>
            delta.Kind == CharacterCreationFinalizationDeltaKinds.Quality
            && delta.TargetId == selectedQuality.SourceId.ToString("D")));
        Assert.IsTrue(review.OrderedDeltas.Any(delta =>
            delta.Kind == CharacterCreationFinalizationDeltaKinds.Gear
            && delta.TargetId == selectedGear.SourceId.ToString("D")));

        CharacterCreationQualitySelection tamperedSelection = selectedQuality with
        {
            SourceNodeXml = selectedQuality.SourceNodeXml.Replace(
                selectedQuality.Name,
                selectedQuality.Name + " tampered",
                StringComparison.Ordinal)
        };
        WorkspaceStoredDocument tamperedWorkspace = before with
        {
            Document = before.Document with
            {
                State = before.Document.State with
                {
                    AuxiliaryState = before.Document.AuxiliaryState with
                    {
                        CharacterCreationQualitiesDraft = qualityDraft with
                        {
                            Selections = [tamperedSelection]
                        }
                    }
                }
            }
        };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(
            tamperedWorkspace,
            out _, out _, out _, out _, out _, out _, out string[] tamperBlockers));
        CollectionAssert.Contains(
            tamperBlockers.ToList(),
            CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        CharacterCreationGearLine tamperedGear = selectedGear with
        {
            SourceNodeXml = selectedGear.SourceNodeXml.Replace(
                selectedGear.Name,
                selectedGear.Name + " tampered",
                StringComparison.Ordinal)
        };
        WorkspaceStoredDocument gearTamperedWorkspace = before with
        {
            Document = before.Document with
            {
                State = before.Document.State with
                {
                    AuxiliaryState = before.Document.AuxiliaryState with
                    {
                        CharacterCreationGearDraft = gearDraft with
                        {
                            Lines = [tamperedGear],
                            FinalizationContribution = gearDraft.FinalizationContribution with
                            {
                                Lines = [tamperedGear]
                            }
                        }
                    }
                }
            }
        };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(
            gearTamperedWorkspace,
            out _, out _, out _, out _, out _, out _, out string[] gearTamperBlockers));
        CollectionAssert.Contains(
            gearTamperBlockers.ToList(),
            CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);

        const string key = "finalize-priority-nonempty-quality-gear-0001";
        CharacterCreationFinalizationConfirmRequest command = new(
            state.Binding,
            review.PreviewDigest,
            review.Plan!.PlanDigest,
            key,
            ExplicitlyConfirmed: true);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> applied =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome,
            string.Join(",", applied.Blockers));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed,
            context.Finalizer.Confirm(command).Outcome);

        using ReadyContext restarted = context.Restart();
        WorkspaceStoredDocument reopened = restarted.Store.Get(restarted.WorkspaceId).Value!;
        XDocument document = XDocument.Parse(reopened.Document.Content, LoadOptions.None);
        XElement root = document.Root!;
        XElement[] qualities = root.Element("qualities")!.Elements("quality").ToArray();
        XElement[] gears = root.Element("gears")!.Elements("gear").ToArray();
        Assert.HasCount(1, qualities);
        Assert.HasCount(1, gears);
        XElement quality = qualities.Single();
        XElement gear = gears.Single();
        Assert.AreEqual(selectedQuality.SourceId.ToString("D"), quality.Element("sourceid")!.Value);
        Assert.AreEqual(selectedQuality.Name, quality.Element("name")!.Value);
        Assert.IsNotNull(quality.Element("bonus"));
        Assert.IsNotNull(quality.Element("firstlevelbonus"));
        XElement[] qualityImprovements = (root.Element("improvements")
                ?.Elements("improvement") ?? Enumerable.Empty<XElement>())
            .Where(item => item.Element("sourcename")?.Value == quality.Element("guid")!.Value)
            .ToArray();
        Assert.IsTrue(qualityImprovements.Length <= 1);
        Assert.AreEqual(selectedGear.SourceId.ToString("D"), gear.Element("sourceid")!.Value);
        Assert.AreEqual(selectedGear.Name, gear.Element("name")!.Value);
        Assert.AreEqual(selectedGear.Quantity, int.Parse(gear.Element("qty")!.Value));
        Assert.IsNotNull(gear.Element("children"));
        Assert.IsNotNull(gear.Element("wirelessbonus"));
        Assert.IsTrue(restarted.Queries.ParseSummary(new(reopened.Document.Content)).Created);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed,
            restarted.Finalizer.LookupReceipt(new(restarted.WorkspaceId, key)).Outcome);
    }

    [TestMethod]
    public void Supported_quality_effect_projects_the_complete_legacy_quality_and_improvement_graph()
    {
        Guid sourceId = Guid.Parse("68cfe94a-fa7e-4129-a9b9-b5d73e3ced99");
        string sourceNodeXml = $"<quality><id>{sourceId:D}</id><name>Exact Ambidextrous</name><karma>4</karma><category>Positive</category><implemented>True</implemented><contributetobp>True</contributetobp><contributetolimit>True</contributetolimit><doublecareer>True</doublecareer><bonus><ambidextrous /></bonus><firstlevelbonus /><source>SR5</source><page>71</page><notes>source note</notes><notesColor>#010203</notesColor></quality>";
        string sourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(
            sourceNodeXml);
        var selection = new CharacterCreationQualitySelection(
            "quality:exact-ambidextrous:rating:1",
            sourceId,
            sourceId.ToString("D"),
            "Exact Ambidextrous",
            CharacterCreationQualityType.Positive,
            Rating: 1,
            KarmaCost: 4,
            IsMetagenic: false,
            CountsAgainstQualityLimit: true,
            CountsAgainstKarma: true,
            IsFreeOrGranted: false,
            FollowUpChoiceId: null,
            FollowUpChoiceLabel: null,
            SourceAnchorIds: [$"qualities.xml#quality:{sourceId:D}"],
            SourceNodeXml: sourceNodeXml,
            SourceNodeDigest: sourceNodeDigest,
            OptionDigest: CharacterCreationFinalizationDigest.ComputeUtf8("option"));
        string draftDigest = CharacterCreationFinalizationDigest.ComputeUtf8("quality-draft");

        Assert.IsTrue(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(
            sourceNodeXml));
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection,
            draftDigest,
            out XElement[] qualities,
            out XElement[] improvements));
        Assert.HasCount(1, qualities);
        Assert.HasCount(1, improvements);
        XElement quality = qualities.Single();
        XElement improvement = improvements.Single();
        Assert.AreEqual(sourceId.ToString("D"), quality.Element("sourceid")!.Value);
        Assert.AreEqual("4", quality.Element("bp")!.Value);
        Assert.AreEqual("Selected", quality.Element("qualitysource")!.Value);
        Assert.AreEqual("source note", quality.Element("notes")!.Value);
        Assert.AreEqual("#010203", quality.Element("notesColor")!.Value);
        Assert.IsNotNull(quality.Element("bonus")!.Element("ambidextrous"));
        Assert.AreEqual("Ambidextrous", improvement.Element("improvementttype")!.Value);
        Assert.AreEqual("Quality", improvement.Element("improvementsource")!.Value);
        Assert.AreEqual(quality.Element("guid")!.Value, improvement.Element("sourcename")!.Value);
        Assert.AreEqual("1", improvement.Element("enabled")!.Value);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection with { SourceNodeDigest = CharacterCreationFinalizationDigest.ComputeUtf8("tampered") },
            draftDigest,
            out _,
            out _));
    }

    [TestMethod]
    public void SumToTen_whole_build_finalization_remains_fail_closed_until_its_typed_lanes_exist()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(
            CharacterCreationBuildMethods.SumToTen);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> result =
            context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.BuildMethodNotReady);
        Assert.IsFalse(result.Value!.CanReview);
    }

    private static T AssertAvailable<T>(CharacterCreationFinalizationResult<T> result)
        where T : class
    {
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, result.Outcome,
            string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private sealed class ReadyContext : IDisposable
    {
        private readonly bool _ownsDirectory;
        private readonly ICharacterSourceDataResolver _resolver;

        private ReadyContext(
            string directory,
            FileWorkspaceStore store,
            CharacterWorkspaceId workspaceId,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            bool ownsDirectory)
        {
            Directory = directory;
            Store = store;
            WorkspaceId = workspaceId;
            Queries = queries;
            _resolver = resolver;
            _ownsDirectory = ownsDirectory;
            Finalizer = BuildFinalizer(store, queries, resolver);
        }

        public string Directory { get; }
        public FileWorkspaceStore Store { get; }
        public CharacterWorkspaceId WorkspaceId { get; }
        public ICharacterFileQueries Queries { get; }
        public ICharacterSourceDataResolver Resolver => _resolver;
        public ICharacterCreationFinalizationService Finalizer { get; }
        public IReadOnlyList<Action<IWorkspaceStore>> ReplayChecks { get; init; } = [];

        public static ReadyContext Create(
            bool includeGearReview,
            bool includeNonEmptyPurchases = false,
            Action<XElement>? beforeDrafts = null,
            string? talentValue = null)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"chummer-creation-finalization-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                string coreRoot = FindCoreRoot();
                ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(
                    new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
                ICharacterFileQueries queries = new XmlCharacterFileQueries(new CharacterFileService());
                var store = new FileWorkspaceStore(directory);
                CharacterWorkspaceId workspaceId = beforeDrafts is null
                    ? Bootstrap(store, queries, resolver)
                    : BootstrapPersistedShapeFixture(store, resolver, beforeDrafts);
                var replayChecks = new List<Action<IWorkspaceStore>>();
                CompleteDrafts(
                    store,
                    workspaceId,
                    queries,
                    resolver,
                    includeGearReview,
                    includeNonEmptyPurchases,
                    replayChecks, talentValue);
                return new ReadyContext(
                    directory,
                    store,
                    workspaceId,
                    queries,
                    resolver,
                    ownsDirectory: true) { ReplayChecks = replayChecks };
            }
            catch
            {
                System.IO.Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public static ReadyContext CreateUnprepared(string buildMethod)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"chummer-creation-finalization-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string coreRoot = FindCoreRoot();
            ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
            ICharacterFileQueries queries = new XmlCharacterFileQueries(new CharacterFileService());
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId workspaceId = Bootstrap(store, queries, resolver, buildMethod);
            return new ReadyContext(
                directory,
                store,
                workspaceId,
                queries,
                resolver,
                ownsDirectory: true);
        }

        public ReadyContext Restart() => new(
            Directory,
            new FileWorkspaceStore(Directory),
            WorkspaceId,
            Queries,
            _resolver,
            ownsDirectory: false);

        public void Dispose()
        {
            if (_ownsDirectory && System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static CharacterWorkspaceId Bootstrap(
            IWorkspaceStore store,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            string buildMethod = CharacterCreationBuildMethods.Priority)
        {
            var codec = new Sr5WorkspaceCodec(
                queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var service = new CharacterCreationBootstrapService(
                store,
                new RulesetWorkspaceCodecResolver([codec]),
                queries,
                resolver);
            Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
                buildMethod,
                out string settingsProfileId));
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result = service.Create(new(
                CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                RulesetDefaults.Sr5,
                "Finalization Runner",
                "Finalizer",
                buildMethod,
                settingsProfileId));
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome,
                string.Join(",", result.Blockers));
            return result.Value!.WorkspaceId;
        }

        private static CharacterWorkspaceId BootstrapPersistedShapeFixture(
            FileWorkspaceStore store, ICharacterSourceDataResolver resolver, Action<XElement> configure)
        {
            // Captured pre-decision SR5 shape for legacy/custom-data cases only.
            // Bind it through the real authority and atomic-create capability;
            // changing XML after bootstrap would correctly invalidate its digest.
            // Ordinary new-runner cases above use the actual Bootstrap service.
            var root = XElement.Parse($"""
                <character>
                  <name>Finalization Runner</name><alias>Finalizer</alias><buildmethod>Priority</buildmethod>
                  <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                  <karma>0</karma><nuyen>0</nuyen><created>False</created><gameedition>SR5</gameedition>
                  <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                  <{CharacterCreationBootstrapXml.MarkerElement}>
                    <{CharacterCreationBootstrapXml.SchemaElement}>{CharacterCreationBootstrapSchemas.MarkerV1}</{CharacterCreationBootstrapXml.SchemaElement}>
                    <{CharacterCreationBootstrapXml.StageElement}>{CharacterCreationBootstrapStages.AwaitingFoundationSelection}</{CharacterCreationBootstrapXml.StageElement}>
                  </{CharacterCreationBootstrapXml.MarkerElement}>
                  <streetcred>0</streetcred><notoriety>0</notoriety><publicawareness>0</publicawareness><burntstreetcred>0</burntstreetcred>
                  <expenses /><improvements /><contacts />
                </character>
                """);
            configure(root);
            var id = new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
            var document = new WorkspaceDocument(new WorkspacePayloadEnvelope("sr5", 1,
                "sr5/chum5-xml", root.ToString(SaveOptions.DisableFormatting)));
            Assert.IsTrue(CharacterCreationBootstrapAuthority.TryPrepareBinding(id, document, resolver,
                out var binding, out _, out var blockers), string.Join(",", blockers));
            document = document with { State = document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: binding)
            } };
            var created = ((ICharacterCreationBootstrapAtomicCreateCapability)store)
                .CreateCharacterCreationBootstrapWorkspaceDocument(id, document);
            Assert.IsTrue(created.Success, created.Error);
            return id;
        }

        private static void CompleteDrafts(
            IWorkspaceStore store,
            CharacterWorkspaceId workspaceId,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            bool includeGearReview,
            bool includeNonEmptyPurchases,
            ICollection<Action<IWorkspaceStore>>? replayChecks = null,
            string? talentValue = null)
        {
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            CharacterCreationPrerequisiteState prerequisite = prerequisites.Load(new(workspaceId)).Value!;
            IReadOnlyDictionary<string, string> ranks = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = talentValue is null ? "A" : "E",
                [CharacterCreationPriorityCategoryIds.Talent] = talentValue is null ? "E" : "B",
                [CharacterCreationPriorityCategoryIds.Attributes] = talentValue is null ? "B" : "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "C",
                [CharacterCreationPriorityCategoryIds.Resources] = "D"
            };
            CharacterCreationPriorityOptionProjection heritageRank = prerequisite.Authority.Options.Single(
                option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                          && option.Rank == ranks[CharacterCreationPriorityCategoryIds.Heritage]);
            CharacterCreationPriorityHeritageOptionProjection heritage = heritageRank.HeritageOptions.First(
                static option => option.IsEnabled
                                 && option.MetavariantSourceId is null
                                 && option.MetatypeName == "Human");
            CharacterCreationPriorityOptionProjection talentRank = prerequisite.Authority.Options.Single(
                option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                          && option.Rank == ranks[CharacterCreationPriorityCategoryIds.Talent]);
            CharacterCreationPriorityTalentOptionProjection talent = talentValue is not null
                ? talentRank.TalentOptions.First(option => option.IsEnabled && option.Value == talentValue)
                : talentRank.TalentOptions.First(
                static option => option.IsEnabled
                                 && string.Equals(option.Value,
                                     CharacterCreationMagicResonanceKinds.Mundane,
                                     StringComparison.OrdinalIgnoreCase)
                                 && option.Magic is null
                                 && option.Resonance is null
                                 && option.Depth is null
                                 && option.ActiveSkillGrant is null
                                 && option.SkillGroupGrant is null);
            string[] talentSkills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            string[] talentGroups = talent.SkillGroupGrant?.Options
                .Take(talent.SkillGroupGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            var prerequisiteRequest = new CharacterCreationPrerequisitePreviewRequest(
                prerequisite.Binding,
                ranks)
            {
                HeritageSelectionId = heritage.SelectionId,
                TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = talentSkills, TalentSkillGroupSelectionIds = talentGroups
            };
            CharacterCreationPrerequisitePreview prerequisitePreview =
                prerequisites.Preview(prerequisiteRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                prerequisites.Confirm(new(
                    prerequisitePreview.Binding,
                    ranks,
                    prerequisitePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = heritage.SelectionId,
                    TalentSelectionId = talent.SelectionId,
                    TalentActiveSkillSelectionIds = talentSkills, TalentSkillGroupSelectionIds = talentGroups
                }).Outcome);

            var attributes = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributeState = attributes.Load(new(workspaceId)).Value!;
            CharacterCreationAttributesPreview attributePreview = attributes.Preview(new(
                attributeState.Binding,
                [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributes.Confirm(new(
                    attributePreview.Binding,
                    [],
                    attributePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)).Outcome);

            var skills = new CharacterCreationSkillsService(store, resolver);
            CharacterCreationSkillsState skillsState = skills.Load(new(workspaceId)).Value!;
            CharacterCreationSkillCatalogEntry native = skillsState.Authority.KnowledgeSkills.First(
                static option => option.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] skillAllocations =
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview skillsPreview = skills.Preview(new(
                skillsState.Binding,
                skillAllocations,
                [])).Value!;
            var skillCommand = new CharacterCreationSkillsConfirmRequest(
                    skillsPreview.Binding,
                    skillAllocations,
                    [],
                    skillsPreview.PreviewDigest,
                    "skills-finalization-test",
                    ExplicitlyConfirmed: true);
            var skillReceipt = skills.Confirm(skillCommand);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, skillReceipt.Outcome);
            replayChecks?.Add(reopenedStore =>
            {
                var service = new CharacterCreationSkillsService(reopenedStore, resolver);
                var replay = service.Confirm(skillCommand);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
                Assert.AreEqual(skillReceipt.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                    service.Confirm(skillCommand with { PreviewDigest = CharacterCreationFinalizationDigest.ComputeUtf8("changed-preview") }).Outcome);
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                    service.Confirm(skillCommand with { IdempotencyKey = "new-career-skill-command" }).Outcome);
            });

            if (talentValue is not null)
            {
                var magicService = new CharacterCreationMagicResonanceService(store, resolver);
                var magicState = magicService.Load(new(workspaceId)).Value!;
                Assert.IsTrue(magicState.CanEdit, string.Join(",", magicState.Blockers));
                var selected = magicState.SelectedTalent!;
                var powers = new List<CharacterCreationAdeptPowerAllocation>();
                decimal remaining = magicState.AdeptPowerPointBudget.Total;
                foreach (var power in magicState.Authority.AdeptPowers.Where(item => item.IsEnabled && item.PointCost > 0)
                    .OrderByDescending(item => item.PointCost))
                {
                    int levels = (int)Math.Min(CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(power, selected.Magic),
                        decimal.Floor(remaining / power.PointCost));
                    if (levels == 0) continue;
                    powers.Add(new(power.Identity, levels));
                    remaining -= levels * power.PointCost;
                }
                Assert.AreEqual(0m, remaining);
                var selections = new CharacterCreationMagicResonanceSelections(
                    selected.RequiresTradition ? magicState.Authority.Traditions.Single(item => item.Name == "Hermetic").Identity : null,
                    selected.RequiresStream ? magicState.Authority.Streams.Single(item => item.Name == "Default").Identity : null,
                    powers,
                    magicState.Authority.Spells.Where(item => item.IsEnabled).Take(selected.SpellBudget).Select(item => item.Identity).ToArray(),
                    magicState.Authority.ComplexForms.Where(item => item.IsEnabled).Take(selected.ComplexFormBudget).Select(item => item.Identity).ToArray());
                var preview = magicService.Preview(new(magicState.Binding, selections)).Value!;
                Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                    magicService.Confirm(new(preview.Binding, selections, preview.PreviewDigest, "finalize-magic-source", true)).Outcome);
            }

            var qualities = new CharacterCreationQualitiesService(
                store, resolver, prerequisites, attributes);
            CharacterCreationQualitiesState qualityState = qualities.Load(new(workspaceId)).Value!;
            string[] selectedQualityIds = includeNonEmptyPurchases
                ?
                [qualityState.Authority.Options
                    .Where(static option => option.IsSelectable)
                    .Where(static option => option.KarmaCost is >= 0 and <= 25)
                    .OrderBy(static option => option.KarmaCost)
                    .ThenBy(static option => option.OptionId, StringComparer.Ordinal)
                    .First().OptionId]
                : [];
            CharacterCreationQualitiesPreview qualityPreview = qualities.Preview(new(
                qualityState.Binding,
                selectedQualityIds)).Value!;
            var qualityCommand = new CharacterCreationQualitiesConfirmRequest(
                    qualityPreview.Binding,
                    selectedQualityIds,
                    qualityPreview.PreviewDigest,
                    "qualities-finalization-test",
                    Guid.NewGuid(),
                    ExplicitlyConfirmed: true);
            CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> qualityReceipt =
                qualities.Confirm(qualityCommand);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                qualityReceipt.Outcome,
                string.Join(",", qualityReceipt.Blockers));
            replayChecks?.Add(reopenedStore =>
            {
                var service = new CharacterCreationQualitiesService(reopenedStore, resolver,
                    new CharacterCreationPrerequisiteService(reopenedStore, queries, resolver),
                    new CharacterCreationAttributesService(reopenedStore, resolver));
                var replay = service.Confirm(qualityCommand);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
                Assert.AreEqual(qualityReceipt.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                    service.Confirm(qualityCommand with { PreviewDigest = CharacterCreationFinalizationDigest.ComputeUtf8("changed-preview") }).Outcome);
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                    service.Confirm(qualityCommand with { IdempotencyKey = "new-career-quality-command" }).Outcome);
            });

            var resources = new CharacterCreationResourcesService(store, resolver);
            CharacterCreationResourcesState resourcesState = resources.Load(new(workspaceId)).Value!;
            CharacterCreationResourceAllocationOption zeroKarma = resourcesState.Options.First(
                static option => option.IsEnabled && option.KarmaInvestment == 0);
            CharacterCreationResourcesPreview resourcePreview = resources.Preview(new(
                resourcesState.Binding,
                zeroKarma.OptionId)).Value!;
            var resourceCommand = new CharacterCreationResourcesConfirmRequest(
                    resourcePreview.Binding,
                    zeroKarma.OptionId,
                    resourcePreview.PreviewDigest,
                    "resources-finalization-test",
                    ExplicitlyConfirmed: true);
            var resourceReceipt = resources.Confirm(resourceCommand);
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resourceReceipt.Outcome);
            replayChecks?.Add(reopenedStore =>
            {
                var service = new CharacterCreationResourcesService(reopenedStore, resolver);
                var lookup = service.LookupReceipt(new(workspaceId, resourceCommand.IdempotencyKey));
                Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, lookup.Outcome, string.Join(",", lookup.Blockers));
                Assert.AreEqual(resourceReceipt.Value!.ReceiptDigest, lookup.Value!.ReceiptDigest);
                Assert.AreEqual(CharacterCreationResourcesOutcomes.Replayed, service.Confirm(resourceCommand).Outcome);
                Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict,
                    service.Confirm(resourceCommand with { PreviewDigest = CharacterCreationFinalizationDigest.ComputeUtf8("changed-preview") }).Outcome);
                Assert.AreNotEqual(CharacterCreationResourcesOutcomes.Applied,
                    service.Confirm(resourceCommand with { IdempotencyKey = "new-career-resource-command" }).Outcome);
            });

            if (!includeGearReview)
                return;
            var gear = new CharacterCreationGearService(store, resolver);
            CharacterCreationGearState gearState = gear.Load(new(workspaceId)).Value!;
            CharacterCreationGearSelection[] basket = includeNonEmptyPurchases
                ?
                [new CharacterCreationGearSelection(
                    gearState.Authority.Options
                        .Where(static option => option.IsSelectable)
                        .Where(option => option.PackageQuantity == 1
                                         && option.PackageCost > 0m
                                         && option.PackageCost <= gearState.Budget.TotalStartingNuyen)
                        .OrderBy(static option => option.PackageCost)
                        .ThenBy(static option => option.OptionId, StringComparer.Ordinal)
                        .First().OptionId,
                    Quantity: 1)]
                : [];
            CharacterCreationGearPreview gearPreview = gear.Preview(new(
                gearState.Binding,
                basket)).Value!;
            var gearCommand = new CharacterCreationGearConfirmRequest(
                    gearPreview.Binding,
                    basket,
                    gearPreview.PreviewDigest,
                    "gear-finalization-test",
                    ExplicitlyConfirmed: true);
            var gearReceipt = gear.Confirm(gearCommand);
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gearReceipt.Outcome);
            replayChecks?.Add(reopenedStore =>
            {
                var service = new CharacterCreationGearService(reopenedStore, resolver);
                var lookup = service.LookupReceipt(new(workspaceId, gearCommand.IdempotencyKey));
                Assert.AreEqual(CharacterCreationGearOutcomes.Available, lookup.Outcome, string.Join(",", lookup.Blockers));
                Assert.AreEqual(gearReceipt.Value!.ReceiptDigest, lookup.Value!.ReceiptDigest);
                Assert.AreEqual(CharacterCreationGearOutcomes.Replayed, service.Confirm(gearCommand).Outcome);
                Assert.AreEqual(CharacterCreationGearOutcomes.Conflict,
                    service.Confirm(gearCommand with { PreviewDigest = CharacterCreationFinalizationDigest.ComputeUtf8("changed-preview") }).Outcome);
                Assert.AreNotEqual(CharacterCreationGearOutcomes.Applied,
                    service.Confirm(gearCommand with { IdempotencyKey = "new-career-gear-command" }).Outcome);
            });
        }

        internal static ICharacterCreationFinalizationService BuildFinalizer(
            IWorkspaceStore store,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver)
        {
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var attributes = new CharacterCreationAttributesService(store, resolver);
            return new CharacterCreationFinalizationService(
                store,
                queries,
                prerequisites,
                attributes,
                new CharacterCreationSkillsService(store, resolver),
                new CharacterCreationQualitiesService(store, resolver, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(store, resolver),
                new CharacterCreationResourcesService(store, resolver),
                new CharacterCreationGearService(store, resolver));
        }

        private static string FindCoreRoot()
        {
            DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate canonical Chummer data.");
        }
    }

    private sealed class CommitThenReportUnavailableStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private readonly FileWorkspaceStore _inner;

        public CommitThenReportUnavailableStore(FileWorkspaceStore inner) => _inner = inner;

        public int AtomicCommitCount { get; private set; }
        public Func<WorkspaceStoredDocument, WorkspaceStoredDocument>? ReadTransform { get; init; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            AtomicCommitCount++;
            WorkspaceStoreMutationResult committed =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)_inner)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision,
                    expectedAuxiliaryStateDigest,
                    document);
            return committed.Success
                ? new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Unavailable,
                    Error: "Simulated lost acknowledgement after durable commit.")
                : committed;
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(owner, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(id, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(owner, id, document);
        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _inner.List(owner);
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => Transform(_inner.Get(id));
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => Transform(_inner.Get(owner, id));
        private WorkspaceStoreReadResult Transform(WorkspaceStoreReadResult result) =>
            result.Value is { } value && ReadTransform is { } transform
                ? result with { Value = transform(value) }
                : result;
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.SaveCheckpoint(id, expectedContentRevision);
        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.SaveCheckpoint(owner, id, expectedContentRevision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.Delete(id, expectedContentRevision);
        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.Delete(owner, id, expectedContentRevision);
    }
}
