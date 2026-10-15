using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class LifestylesCompletionTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Lifestyle_completion_before_attributes_or_resources_uses_real_prices_without_saving(string method)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(method);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputs = LoadInputs(context);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsNull(inputs.Lifestyles.Gear.Resources.PendingDraft);
        Assert.IsNull(inputs.Lifestyles.Gear.State.PendingDraft);
        Assert.AreEqual(0, inputs.Lifestyles.Lifestyles.Count);
        Assert.AreEqual(inputs.Lifestyles.SnapshotDigest,
            CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(inputs.Lifestyles));
        var low = Configuration(inputs.Lifestyles.Authority, "Low");

        var result = Preview(inputs, [low]);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projected = result.Value!;
        var qualities = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
            inputs.Lifestyles.Qualities, Attributes(inputs), inputs.Lifestyles.Qualities.SelectedOptionIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(qualities.Value, projected.Qualities));
        Assert.AreEqual(inputs.Lifestyles.Binding, projected.Binding);
        Assert.AreEqual(inputs.Lifestyles.SnapshotDigest, projected.LifestylesSnapshotDigest);
        Assert.AreEqual(low.LifestyleId, projected.StartingLifestyleId);
        Assert.AreEqual(2000m, projected.Lifestyles.Single().Economics.CostPerIncrement);
        Assert.AreEqual(2000m, projected.Budget.Used);
        Assert.AreEqual(projected.Gear.Budget.RemainingNuyen, projected.Budget.Total);
        Assert.AreEqual(projected.Budget.Total - 2000m, projected.Budget.Remaining);
        Assert.AreEqual(projected.Budget.Remaining, projected.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(2000m, projected.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(0m, projected.Budget.Overspend);
        Assert.IsTrue(projected.Budget.IsExact);
        Assert.IsNull(projected.GetType().GetProperty("CanConfirm"));
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(low, projected.Authority,
            out var ordinaryRow, out var blockers), string.Join(",", blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            ordinaryRow, projected.Lifestyles.Single()));
        Assert.AreEqual(projected.ProposalDigest,
            CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projected));
        Assert.AreEqual(projected.ProposalDigest, Preview(inputs, [low]).Value!.ProposalDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, 1)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 1)]
    [DataRow(CharacterCreationBuildMethods.Priority, 10_000)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 10_000)]
    public void Lifestyle_completion_matches_independent_quality_evaluation_and_keeps_results_detached(string method, int increments)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(method);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        // Ambidextrous's anatomy-dependent limit is not admitted for this
        // Mystic Adept. Use the same fixed-cost source quality as the combined
        // magic/quality budget fixture, without relaxing selection admission.
        var paid = inputs.Lifestyles.Qualities.Authority.Options.Single(option =>
            option.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && option.Rating == 1);
        Assert.AreEqual("Guts", paid.Name);
        Assert.IsTrue(paid.IsSelectable, paid.DisableReasonKey);
        Assert.IsTrue(paid.CountsAgainstKarma);
        Assert.AreEqual(10, paid.KarmaCost);
        var qualityIds = new List<string> { paid.OptionId };
        CharacterCreationAttributeAllocation[] attributes = [.. Attributes(inputs), new("BOD", 0, 1)];
        var home = Configuration(inputs.Lifestyles.Authority, "Low") with { Increments = increments };
        var independent = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
            inputs.Lifestyles.Qualities, attributes, qualityIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, independent.Outcome, string.Join(",", independent.Blockers));
        Assert.IsTrue(independent.Value!.Qualities.ProposedAttributeKarmaUsed > 0);
        Assert.AreEqual(inputs.Lifestyles.Qualities.Binding.CreationKarmaTotal
            - independent.Value.Qualities.ProposedAttributeKarmaUsed - paid.KarmaCost, independent.Value.Qualities.KarmaRemaining);

        var result = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            attributes, Selections(inputs), qualityIds, 0, [], [home], home.LifestyleId);
        Assert.AreEqual(increments == 1 ? CharacterCreationLifestyleOutcomes.Available : CharacterCreationLifestyleOutcomes.Blocked,
            result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        var projected = result.Value!;
        var upstreamQualities = projected.Gear.Resources.UpstreamBudget.Qualities;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(independent.Value, projected.Qualities));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(independent.Value, upstreamQualities));
        Assert.AreEqual(inputs.Lifestyles.Binding, projected.Binding);
        Assert.AreEqual(inputs.Lifestyles.Authority.SourceDigest, projected.Authority.SourceDigest);
        Assert.AreEqual(inputs.Lifestyles.Qualities.SnapshotDigest, projected.Qualities.QualitiesSnapshotDigest);
        Assert.AreEqual(2000m * increments, projected.Budget.Used);
        Assert.AreEqual(projected.Budget.Used, projected.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(projected.Budget.Remaining, projected.Gear.Resources.Budget.RemainingNuyen);
        if (increments > 1)
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        Assert.AreEqual(projected.ProposalDigest, CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projected));
        var repeated = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            attributes, Selections(inputs), qualityIds, 0, [], [home], home.LifestyleId);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(result, repeated));

        string digest = projected.ProposalDigest;
        qualityIds.Clear();
        attributes[0] = new("MAG", 0, 0);
        Assert.AreEqual(digest, CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projected));
        Assert.AreNotSame(projected.Qualities, upstreamQualities);
        Assert.AreNotSame(projected.Qualities.Qualities.Selections, upstreamQualities.Qualities.Selections);
        var selections = (IList<CharacterCreationQualitySelection>)upstreamQualities.Qualities.Selections;
        selections[0] = selections[0] with { Name = "Changed after preview" };
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(independent.Value, projected.Qualities));
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    [DataRow("source", CharacterCreationLifestyleOutcomes.Blocked, CharacterCreationQualitiesBlockers.AuthorityUnavailable)]
    [DataRow("catalog", CharacterCreationLifestyleOutcomes.Blocked, CharacterCreationQualitiesBlockers.AuthorityUnavailable)]
    [DataRow("profile", CharacterCreationLifestyleOutcomes.Conflict, CharacterCreationResourcesCompletionSchemas.SnapshotConflict)]
    [DataRow("selection", CharacterCreationLifestyleOutcomes.Blocked, CharacterCreationQualitiesBlockers.InvalidSelection)]
    public void Lifestyle_completion_rejects_quality_mismatches_before_lifestyle_pricing(string mismatch, string outcome, string blocker)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var authority = inputs.Lifestyles.Qualities.Authority;
        string foreignDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("foreign-quality-source");
        if (mismatch == "source")
            authority = authority with { SourceDigest = foreignDigest };
        else if (mismatch == "profile")
            authority = authority with { ProfileDigest = foreignDigest };
        else if (mismatch == "catalog")
        {
            var options = authority.Options.ToArray();
            options[0] = options[0] with { SourceNodeDigest = foreignDigest };
            authority = authority with { Options = options };
        }
        authority = authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
        var qualities = inputs.Lifestyles.Qualities with { Authority = authority };
        if (mismatch != "source")
            qualities = qualities with { Binding = qualities.Binding with { AuthorityDigest = authority.AuthorityDigest } };
        qualities = qualities with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(qualities) };
        var lifestyles = inputs.Lifestyles with { Qualities = qualities };
        lifestyles = lifestyles with { SnapshotDigest = CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(lifestyles) };
        var changed = inputs with { Lifestyles = lifestyles };
        string[] qualityIds = mismatch == "selection" ? ["not-a-source-quality"] : [];
        var invalidHome = Configuration(inputs.Lifestyles.Authority, "Low") with { BaseLifestyleOptionId = "not-a-source-lifestyle" };

        var gear = CharacterCreationGearService.PreviewCompletion(lifestyles.Gear, inputs.Magic, qualities,
            Attributes(changed), Selections(changed), qualityIds, 0, []);
        Assert.IsNull(gear.Value);
        var rejected = Preview(changed, [invalidHome], qualityIds: qualityIds);
        Assert.AreEqual(outcome, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), blocker);
        CollectionAssert.AreEqual(gear.Blockers.Append(CharacterCreationLifestylesCompletionSchemas.UpstreamBudgetUnavailable)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), rejected.Blockers.ToArray());
        CollectionAssert.DoesNotContain(rejected.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InvalidOption);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Lifestyle_completion_rejects_unaffordable_invalid_and_unowned_trust_fund_choices()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var low = Configuration(inputs.Lifestyles.Authority, "Low");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var expensive = Preview(inputs, [low with { Increments = 10_000 }]);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Blocked, expensive.Outcome);
        Assert.IsNotNull(expensive.Value);
        CollectionAssert.Contains(expensive.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        Assert.AreEqual(20_000_000m, expensive.Value!.Budget.Used);
        Assert.AreEqual(0m, expensive.Value.Budget.Remaining);
        Assert.AreEqual(expensive.Value.Budget.Used - expensive.Value.Budget.Total,
            expensive.Value.Budget.Overspend);

        var invalid = Preview(inputs, [low with { BaseLifestyleOptionId = "not-a-source-lifestyle" }]);
        Assert.IsFalse(invalid.Success);
        CollectionAssert.Contains(invalid.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InvalidOption);
        Assert.AreEqual(0, inputs.Lifestyles.Authority.TrustFundLevel);
        var unowned = Preview(inputs, [low with { TrustFund = true }]);
        Assert.IsFalse(unowned.Success);
        CollectionAssert.Contains(unowned.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InvalidMutation);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Lifestyle_completion_cannot_enable_real_trust_fund_with_unadmitted_quality_requirements()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var trustFund = inputs.Lifestyles.Qualities.Authority.Options
            .Single(option => option.Name == "Trust Fund I" && option.Rating == 1);
        var source = XElement.Parse(trustFund.SourceNodeXml);
        Assert.AreEqual("1", source.Element("bonus")!.Element("trustfund")!.Value);
        Assert.IsNotNull(source.Element("required"), "The real Trust Fund source requires a SINner quality.");
        Assert.IsFalse(trustFund.IsSelectable);
        Assert.IsFalse(trustFund.EligibilityIsExact);
        Assert.IsNotNull(trustFund.DisableReasonKey);
        var home = Configuration(inputs.Lifestyles.Authority, "Medium") with { TrustFund = true };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        Assert.AreEqual(0, inputs.Lifestyles.Authority.TrustFundLevel);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationQualitiesDraft);

        var result = Preview(inputs, [home], qualityIds: [trustFund.OptionId]);
        Assert.IsFalse(result.Success);
        CollectionAssert.Contains(result.Blockers.ToArray(), trustFund.DisableReasonKey!);
        var paid = Preview(inputs, [home with { TrustFund = false }], qualityIds: [trustFund.OptionId]);
        Assert.IsFalse(paid.Success, "Paying rent cannot admit the unsupported character-quality requirement.");
        CollectionAssert.Contains(paid.Blockers.ToArray(), trustFund.DisableReasonKey!);
        Assert.AreEqual(0, inputs.Lifestyles.Authority.TrustFundLevel);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Lifestyle_completion_replaces_cold_saved_purchase_once_and_retains_original_history()
    {
        using ReadyContext context = ReadyContext.Create(true, buildMethod: CharacterCreationBuildMethods.Priority);
        var savedReceipt = SaveLowLifestyle(context);
        using ReadyContext cold = context.Restart();
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var inputs = LoadInputs(cold);
        var original = inputs.Lifestyles.Lifestyles.Single();
        Assert.AreEqual(1, original.Increments);
        Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationLifestylesDraft!.DraftDigest,
            inputs.Lifestyles.Gear.Resources.LifestylesDraft!.DraftDigest);
        Assert.AreEqual(savedReceipt.ReceiptDigest,
            inputs.Lifestyles.Gear.Resources.LifestyleReceipts.Single().Receipt.ReceiptDigest);
        int investment = inputs.Lifestyles.Gear.Resources.PendingDraft!.KarmaInvestment;

        var replacement = original with { Increments = 2 };
        var result = Preview(inputs, [replacement], investment: investment);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projected = result.Value!;
        Assert.AreEqual(0m, projected.Gear.Budget.BasketCost);
        Assert.AreEqual(4000m, projected.Budget.Used);
        Assert.AreEqual(4000m, projected.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(projected.Gear.Resources.Budget.TotalStartingNuyen - 4000m, projected.Budget.Remaining,
            "The full proposed lifestyle set replaces the old apartment; it must not charge old and new months together.");
        Assert.AreEqual(projected.Budget.Remaining, projected.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(1, inputs.Lifestyles.Lifestyles.Single().Increments);
        Assert.AreEqual(savedReceipt.ReceiptDigest,
            LoadInputs(cold).Lifestyles.Gear.Resources.LifestyleReceipts.Single().Receipt.ReceiptDigest);
        AssertWorkspaceUnchanged(cold, before, files);
    }

    [TestMethod]
    public void Lifestyle_completion_rejects_resealed_foreign_source_mixed_snapshot_bad_digest_and_cancellation()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var home = Configuration(inputs.Lifestyles.Authority, "Low");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var corrupt = inputs with { Lifestyles = inputs.Lifestyles with
        {
            SnapshotDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("not-admitted")
        } };
        Assert.IsNull(Preview(corrupt, [home]).Value);
        var foreignAuthority = inputs.Lifestyles.Authority with
        {
            SourceDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("unrelated-lifestyle-source")
        };
        foreignAuthority = foreignAuthority with
        {
            AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(foreignAuthority)
        };
        var foreign = inputs.Lifestyles with { Authority = foreignAuthority };
        foreign = foreign with { SnapshotDigest = CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(foreign) };
        var rejected = Preview(inputs with { Lifestyles = foreign }, [home]);
        Assert.IsFalse(rejected.Success);
        Assert.IsNull(rejected.Value, "Rehashing an authority must not detach it from the original source binding.");
        bool canceled = false;
        try { Preview(inputs, [home], cancellationToken: new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertWorkspaceUnchanged(context, before, files);

        MagicResonanceCompletionTests.SaveAttributes(context, Attributes(inputs));
        var current = LoadInputs(context);
        Assert.AreNotEqual(inputs.Lifestyles.Binding.ContentRevision, current.Lifestyles.Binding.ContentRevision);
        before = context.Store.Get(context.WorkspaceId).Value!;
        files = CaptureFiles(context);
        var mixed = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, current.Magic,
            Attributes(inputs), Selections(inputs), [], 0, [], [home], home.LifestyleId);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Conflict, mixed.Outcome);
        Assert.IsNull(mixed.Value);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Lifestyle_completion_proposal_is_not_confirm_authority_and_result_is_detached()
    {
        using ReadyContext context = ReadyContext.Create(true, buildMethod: CharacterCreationBuildMethods.Priority);
        var inputs = LoadInputs(context);
        var home = Configuration(inputs.Lifestyles.Authority, "Low");
        var requested = new List<CharacterCreationLifestyleConfiguration> { home };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var result = Preview(inputs, requested, investment: inputs.Lifestyles.Gear.Resources.PendingDraft!.KarmaInvestment);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projected = result.Value!;
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
            home.LifestyleId, home);
        var rejected = service.Confirm(new(projected.Binding, mutation, projected.ProposalDigest,
            "completion-is-not-lifestyle-confirm", true));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Conflict, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationLifestylesBlockers.PreviewDigestMismatch);
        string digest = CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projected);
        requested[0] = home with { Increments = 10_000, Name = "Changed after preview" };
        requested.Clear();
        Assert.AreEqual(digest, CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projected));
        Assert.AreEqual(1, projected.Lifestyles.Single().Configuration.Increments);
        Assert.AreEqual(home.Name, projected.Lifestyles.Single().Configuration.Name);
        AssertWorkspaceUnchanged(context, before, files);
    }

    private static CharacterCreationLifestyleConfiguration Configuration(CharacterCreationLifestylesAuthority authority, string name)
    {
        var option = authority.LifestyleOptions.Single(item => item.Name == name && item.IsSelectable);
        return new(Guid.Parse("0da228b4-b41c-4290-af63-ea82631f1e16"), option.OptionId, "Completion apartment",
            CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
    }

    private static CharacterCreationLifestyleReceipt SaveLowLifestyle(ReadyContext context)
    {
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        var home = Configuration(state.Authority, "Low");
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
            home.LifestyleId, home);
        var preview = service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(2000m, preview.BudgetAfter.Used);
        var result = service.Confirm(new(state.Binding, mutation, preview.PreviewDigest,
            "completion-original-lifestyle", true));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static CharacterCreationAttributeAllocation[] Attributes(CompletionInputs inputs)
        => inputs.Lifestyles.Qualities.Attributes.PendingDraft?.Allocations.ToArray() ?? [new("MAG", 1, 0)];

    private static CharacterCreationMagicResonanceSelections Selections(CompletionInputs inputs)
        => inputs.Lifestyles.Qualities.Attributes.PendingDraft is not null
            ? inputs.Magic.Selections
            : MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
                inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);

    private static CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionProjection> Preview(
        CompletionInputs inputs, IReadOnlyList<CharacterCreationLifestyleConfiguration> lifestyles,
        int investment = 0, IReadOnlyList<string>? qualityIds = null, CancellationToken cancellationToken = default)
        => CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            Attributes(inputs), Selections(inputs), qualityIds ?? inputs.Lifestyles.Qualities.SelectedOptionIds,
            investment, [], lifestyles, lifestyles.FirstOrDefault()?.LifestyleId, cancellationToken);

    private static CompletionInputs LoadInputs(ReadyContext context)
    {
        var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver, context.Queries)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, lifestyles.Outcome, string.Join(",", lifestyles.Blockers));
        var magic = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        return new(lifestyles.Value!, magic.Value!);
    }

    private sealed record CompletionInputs(CharacterCreationLifestylesCompletionInputs Lifestyles,
        CharacterCreationMagicResonanceCompletionInputs Magic);

    private static IReadOnlyDictionary<string, byte[]> CaptureFiles(ReadyContext context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkspaceUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        var currentFiles = CaptureFiles(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), currentFiles.Keys.ToArray());
        foreach (var pair in files)
            CollectionAssert.AreEqual(pair.Value, currentFiles[pair.Key], pair.Key);
    }
}
