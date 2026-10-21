using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class SelectedCompletionPricingTests
{
    [TestMethod]
    public void Selected_pricing_matches_full_review_and_replaces_saved_purchase_totals_once()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        using var cold = context.Restart();
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(cold);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value!;
        var lifestyle = review.Lifestyles!;
        var gear = lifestyle.Gear;
        var resources = gear.Resources;
        var attributes = review.Skills!.Attributes;
        var qualityInputs = inputs.Lifestyles.Qualities;
        Assert.IsTrue(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(qualityInputs.Authority,
            qualityInputs.SelectedOptionIds, choices.QualityOptionIds, out var witness));
        Assert.IsNotNull(witness);
        Assert.IsTrue(CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness, choices.QualityOptionIds,
            checked((int)attributes.CreationKarmaBudget.Total), checked((int)attributes.CreationKarmaBudget.Used), out var quality));
        Assert.IsNotNull(quality);
        Assert.AreEqual(0, quality.Blockers.Count);
        Assert.IsTrue(CharacterCreationPriorityKarmaBudget.TryEvaluate(quality.KarmaRemaining, 0,
            review.UpstreamBudget!.Magic.MysticAdeptPowerPoints, out int available));

        var lifestyleInput = new SelectedCompletionLifestylesPricingInput(inputs.Lifestyles.Authority,
            qualityInputs.PrerequisiteDraft, inputs.Magic.SelectedTalent.GrantedQualitySources ?? [],
            quality.Selections, lifestyle.Qualities.ProposalDigest, choices.Lifestyles, choices.StartingLifestyleId,
            gear.Budget.RemainingNuyen, gear.SourceAnchorIds, gear.Blockers);
        var pricedLifestyle = CharacterCreationLifestylesService.PriceSelectedCompletion(lifestyleInput);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, pricedLifestyle.Outcome,
            string.Join(",", pricedLifestyle.Blockers));
        var home = pricedLifestyle.Value!;
        Equal(lifestyle.Authority, home.Authority);
        Equal(lifestyle.Lifestyles, home.Lifestyles);
        Equal(lifestyle.Budget, home.Budget);
        Equal(lifestyle.SourceAnchorIds, home.SourceAnchorIds);
        Assert.AreEqual(2000m, home.FullLifestyleCost);
        Assert.AreEqual(lifestyle.StartingLifestyleId, home.StartingLifestyleId);

        var resourceInput = new SelectedCompletionResourcesPricingInput(inputs.Lifestyles.Gear.Resources.Authority,
            inputs.Lifestyles.Gear.Resources.PriorityOption, available, choices.TotalKarmaInvestment, true,
            checked(gear.Budget.BasketCost + home.FullLifestyleCost), review.UpstreamBudget.Blockers);
        var pricedResources = CharacterCreationResourcesService.PriceSelectedCompletion(resourceInput);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, pricedResources.Outcome,
            string.Join(",", pricedResources.Blockers));
        var money = pricedResources.Value!;
        Equal(resources.Options, money.Options);
        Equal(resources.SelectedOption, money.SelectedOption);
        Equal(resources.Budget, money.Budget);
        Assert.AreEqual(resources.RemainingCreationKarma, money.RemainingCreationKarma);
        Assert.AreEqual(2100m, money.Budget.KnownPurchaseCost);
        Assert.AreEqual(56000m, money.Budget.TotalStartingNuyen);
        Assert.AreEqual(7, money.RemainingCreationKarma);

        var contactInput = new SelectedCompletionContactsPricingInput(inputs.Contacts.Policy,
            qualityInputs.PrerequisiteDraft, inputs.Contacts.TalentQualitySources, inputs.Contacts.OriginalEffects,
            inputs.Contacts.Contacts, attributes.Attributes, quality.Selections, choices.Contacts,
            inputs.Contacts.Binding.SourceDigest, review.Contacts!.Upstream.Blockers,
            qualityInputs.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
            qualityInputs.Authority.QualityKarmaLimit, qualityInputs.Authority.MayExceedPositiveQualityLimit);
        var pricedContacts = CharacterCreationContactsService.PriceSelectedCompletion(contactInput);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, pricedContacts.Outcome,
            string.Join(",", pricedContacts.Blockers));
        Equal(review.Contacts.Contacts, pricedContacts.Value!.Contacts);
        Equal(review.Contacts.ContactBudget, pricedContacts.Value.ContactBudget);
        Equal(review.Contacts.HighPlacesBudget, pricedContacts.Value.HighPlacesBudget);
        Assert.AreEqual(review.Contacts.AdditionalKarmaCost, pricedContacts.Value.AdditionalKarmaCost);

        // A replacement total is not added to already observed purchases again.
        var emptyHome = CharacterCreationLifestylesService.PriceSelectedCompletion(lifestyleInput with
            { Lifestyles = [], StartingLifestyleId = null });
        Assert.AreEqual(0m, emptyHome.Value!.FullLifestyleCost);
        var replacement = CharacterCreationResourcesService.PriceSelectedCompletion(resourceInput with
            { FullPurchaseCost = gear.Budget.BasketCost + emptyHome.Value.FullLifestyleCost });
        Assert.AreEqual(100m, replacement.Value!.Budget.KnownPurchaseCost);
        Assert.AreEqual(7, replacement.Value.RemainingCreationKarma);
        // This arithmetic does not grant permission to delete a retained choice;
        // preservation is still checked by the whole reviewed transaction.

        var noStartingHome = CharacterCreationLifestylesService.PriceSelectedCompletion(lifestyleInput with
            { StartingLifestyleId = null });
        CollectionAssert.Contains(noStartingHome.Blockers.ToArray(), CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);
        var unaffordableHome = CharacterCreationLifestylesService.PriceSelectedCompletion(lifestyleInput with
            { RemainingNuyenAfterGear = 0m });
        CollectionAssert.Contains(unaffordableHome.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        Assert.AreEqual(home.FullLifestyleCost, unaffordableHome.Value!.FullLifestyleCost);
        var overspent = CharacterCreationResourcesService.PriceSelectedCompletion(resourceInput with
            { FullPurchaseCost = money.Budget.TotalStartingNuyen + 1m });
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientNuyen);
        Assert.AreEqual(1m, overspent.Value!.Budget.Overspend);
        var karmaShort = CharacterCreationResourcesService.PriceSelectedCompletion(resourceInput with { AvailableKarma = 0 });
        CollectionAssert.Contains(karmaShort.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientCreationKarma);

        var group = choices.Contacts.Single() with { IsGroup = true };
        var groupPricing = CharacterCreationContactsService.PriceSelectedCompletion(contactInput with { Contacts = [group] });
        Assert.IsFalse(groupPricing.Blockers.Contains(CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable));
        Assert.IsNotNull(groupPricing.Value!.GroupKarma);
        Assert.AreEqual((int?)groupPricing.Value.GroupKarma.AdditionalKarmaCost, groupPricing.Value.AdditionalKarmaCost);
        var groupPublic = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
            choices.AttributeAllocations, choices.QualityOptionIds, [group]);
        Equal(groupPublic.Blockers, groupPricing.Blockers);
        Equal(groupPublic.Value!.ContactBudget, groupPricing.Value.ContactBudget);
        Equal(groupPublic.Value.Contacts, groupPricing.Value.Contacts);
        Equal(groupPublic.Value.GroupKarma, groupPricing.Value.GroupKarma);
        Assert.AreEqual(groupPublic.Value.AdditionalKarmaCost, groupPricing.Value.AdditionalKarmaCost);

        // A semantic effect fixture exercises the shared price kernel only;
        // it is not asserted to be admitted saved history or write authority.
        var originalContact = choices.Contacts.Single();
        var forcedLoyaltyInput = contactInput with
        {
            OriginalEffects = [.. contactInput.OriginalEffects,
                new CharacterContactRuleEffect("ContactForcedLoyalty", originalContact.ContactId.ToString("D"),
                    "3", string.Empty, false, 1, "create")],
            Contacts = [originalContact with { Loyalty = 2 }]
        };
        var forbiddenLoyalty = CharacterCreationContactsService.PriceSelectedCompletion(forcedLoyaltyInput);
        CollectionAssert.Contains(forbiddenLoyalty.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactInvalid);
        CollectionAssert.Contains(forbiddenLoyalty.Blockers.ToArray(), CharacterCreationContactsBlockers.FieldNotEditable);
        var retainedLoyalty = CharacterCreationContactsService.PriceSelectedCompletion(forcedLoyaltyInput with
            { Contacts = [originalContact with { Loyalty = 3 }] });
        Assert.IsFalse(retainedLoyalty.Blockers.Contains(CharacterCreationContactsBlockers.FieldNotEditable));
        Assert.AreEqual(3, retainedLoyalty.Value!.Contacts.Single().Loyalty);

        Assert.Throws<OperationCanceledException>(() => CharacterCreationResourcesService.PriceSelectedCompletion(resourceInput, new(true)));
        Assert.Throws<OperationCanceledException>(() => CharacterCreationLifestylesService.PriceSelectedCompletion(lifestyleInput, new(true)));
        Assert.Throws<OperationCanceledException>(() => CharacterCreationContactsService.PriceSelectedCompletion(contactInput, new(true)));
        var after = cold.Store.Get(cold.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        Equal(before.Document.AuxiliaryState, after.Document.AuxiliaryState);
    }

    private static void Equal<T>(T expected, T actual)
        => Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
