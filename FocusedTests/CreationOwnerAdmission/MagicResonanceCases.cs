using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

internal static class MagicResonanceCases
{
    public static void Run(string coreRoot)
    {
        foreach (OwnerScope owner in new[] { CreationFixture.AccountA, OwnerScope.LocalSingleUser })
            RunPartition(coreRoot, owner);
    }

    private static void RunPartition(string coreRoot, OwnerScope owner)
    {
        using var fixture = new CreationFixture(coreRoot, owner);
        OwnerContextStamp original = fixture.Authority.Capture();
        var bootstrap = fixture.Provider.GetRequiredService<IOwnerBoundCharacterCreationBootstrapService>();
        var created = bootstrap.Create(original, CreationFixture.Request());
        Check.That(created.Value is not null, "Magic bootstrap: " + string.Join(",", created.Blockers));
        var id = created.Value!.WorkspaceId;
        if (!owner.IsLocalSingleUser)
            Check.That(fixture.Provider.GetRequiredService<ICharacterCreationMagicResonanceService>()
                .Load(new(id)).Value is null, "Regression fixture must be absent from the legacy unscoped store.");

        // Same ID in other partitions must neither satisfy readiness nor receive this draft.
        foreach (OwnerScope other in CreationFixture.Partitions.Where(item => item != owner))
        {
            var source = fixture.Read(owner, id).Value!.Document;
            var document = new Chummer.Contracts.Workspaces.WorkspaceDocument(
                source.Content, source.RulesetId, source.Format);
            Check.That((other.IsLocalSingleUser
                ? fixture.Store.CreateWorkspaceDocument(id, document)
                : fixture.Store.CreateWorkspaceDocument(other, id, document)).Success, "Control fixture creation failed.");
        }
        var controls = CreationFixture.Partitions.Where(item => item != owner)
            .ToDictionary(item => item, item => fixture.Snapshot(item, id));

        var prerequisites = fixture.Provider.GetRequiredService<IOwnerBoundCharacterCreationPrerequisiteService>();
        var initial = prerequisites.Load(original, new(id)).Value!;
        var ranks = new Dictionary<string, string>
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "E",
            [CharacterCreationPriorityCategoryIds.Talent] = "C",
            [CharacterCreationPriorityCategoryIds.Attributes] = "A",
            [CharacterCreationPriorityCategoryIds.Skills] = "B",
            [CharacterCreationPriorityCategoryIds.Resources] = "D"
        };
        var heritage = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && item.Rank == "E").HeritageOptions.First(item => item.IsEnabled && item.MetatypeName == "Human"
                && item.MetavariantSourceId is null);
        var talent = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && item.Rank == "C").TalentOptions.First(item => item.IsEnabled && item.Value == "Magician");
        var priority = prerequisites.Preview(original, new(initial.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        Check.That(priority.CanConfirm, "Priority: " + string.Join(",", priority.Blockers));
        Check.That(prerequisites.Confirm(original, new(priority.Binding, ranks, priority.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value is not null,
            "Priority confirmation failed.");
        var attributes = fixture.Provider.GetRequiredService<IOwnerBoundCharacterCreationAttributesService>();
        var attributeState = attributes.Load(original, new(id)).Value!;
        var attributePreview = attributes.Preview(original, new(attributeState.Binding, [])).Value!;
        Check.That(attributePreview.CanConfirm, "Attributes: " + string.Join(",", attributePreview.Blockers));
        Check.That(attributes.Confirm(original, new(attributePreview.Binding, [], attributePreview.PreviewDigest, true))
            .Value is not null, "Attributes confirmation failed.");

        var wrapped = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out var probe);
        var provider = fixture.AddProvider(wrapped, fixture.Authority);
        var service = provider.GetRequiredService<IOwnerBoundCharacterCreationMagicResonanceService>();
        var loaded = service.Load(original, new(id));
        Check.That(loaded.Value is { CanEdit: true }, "Owner Magic load: " + string.Join(",", loaded.Blockers));
        var state = loaded.Value!;
        var selections = new CharacterCreationMagicResonanceSelections(
            state.Authority.Traditions.Single(item => item.Name == "Hermetic").Identity, null, [],
            state.Authority.Spells.Where(item => new[] { "Manabolt", "Powerbolt", "Stunbolt", "Manaball", "Stunball" }
                .Contains(item.Name, StringComparer.Ordinal)).Select(item => item.Identity).ToArray(), []);
        var preview = service.Preview(original, new(state.Binding, selections)).Value!;
        Check.That(preview.CanConfirm, "Magic preview: " + string.Join(",", preview.Blockers));
        var request = new CharacterCreationMagicResonanceConfirmRequest(
            preview.Binding, selections, preview.PreviewDigest, "owner-magic-command", true);
        var before = fixture.Read(owner, id).Value!;

        foreach (OwnerContextStamp invalid in new[] { default, original with { AuthorityInstanceId = "foreign" },
            original with { TransitionRevision = -1 }, original with { Owner = CreationFixture.AccountB } })
            Reject(invalid);
        fixture.Authority.Transition(CreationFixture.AccountB);
        Reject(original);
        fixture.Authority.Transition(owner);
        Reject(original);
        OwnerContextStamp fresh = fixture.Authority.Capture();
        var committed = service.Confirm(fresh, request);
        Check.That(committed.Value is not null, "Owner Magic confirm: " + string.Join(",", committed.Blockers));
        var after = fixture.Read(owner, id).Value!;
        Check.That(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
            && after.Document.Content == before.Document.Content
            && after.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts?.Count == 1,
            "Magic must checkpoint exactly once without rewriting character XML.");
        foreach (var control in controls)
            Check.That(fixture.Snapshot(control.Key, id) == control.Value, "Magic changed another owner's runner.");

        var coldOwner = new ControlledOwner(owner);
        var cold = fixture.AddProvider(new FileWorkspaceStore(fixture.StateDirectory), coldOwner)
            .GetRequiredService<IOwnerBoundCharacterCreationMagicResonanceService>();
        var reopened = cold.Load(coldOwner.Capture(), new(id));
        Check.That(reopened.Value is { CanEdit: true, PendingDraft: not null }
            && reopened.Value.Binding.ContentRevision == after.ContentRevision, "Cold Magic draft did not reopen.");
        Check.That(JsonSerializer.Serialize(cold.Confirm(coldOwner.Capture(), request).Value)
            == JsonSerializer.Serialize(committed.Value), "Cold replay lost the original receipt.");
        Check.That(fixture.Read(owner, id).Value!.ContentRevision == after.ContentRevision,
            "Receipt recovery applied Magic twice.");
        Check.That(fixture.Authority.ActiveLeases == 0 && coldOwner.ActiveLeases == 0, "Magic leaked a lease.");
        ReReviewAfterAttributes();
        Console.WriteLine("PASS Magic " + (owner.IsLocalSingleUser ? "local" : "scoped")
            + ": source catalog, ABA, explicit Attributes re-review, cold reopen and replay");

        void ReReviewAfterAttributes()
        {
            ChangeBody(1);
            var stale = service.Load(fresh, new(id));
            Check.That(stale.Value is { CanEdit: false }
                && stale.Blockers.Contains(CharacterCreationMagicResonanceBlockers.DraftInvalid),
                "Ordinary Magic editing must reject an old Attributes binding.");
            var reviewService = provider.GetRequiredService<IOwnerBoundCharacterCreationMagicResonanceReReviewService>();
            Check.That(ReferenceEquals(service, reviewService), "Re-review DI must share the owner-bound service.");
            string unchanged = fixture.Snapshot(owner, id);
            var loadedReview = reviewService.LoadReReview(fresh, new(id));
            Check.That(loadedReview.Value is not null, "Re-review load: " + string.Join(",", loadedReview.Blockers));
            var review = loadedReview.Value!;
            Check.That(review.InitialPreview.CurrentPreview.CanConfirm,
                "Current re-review preview: " + string.Join(",", review.InitialPreview.CurrentPreview.Blockers));
            Check.That(review.HistoricalDraft.AttributesDraftRevision < review.CurrentState.AttributesDraft!.DraftRevision
                && JsonSerializer.Serialize(review.HistoricalDraft.Selections)
                    == JsonSerializer.Serialize(after.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.Selections),
                "Re-review must retain the saved canonical selection, not replace it with input order.");
            Check.That(fixture.Snapshot(owner, id) == unchanged, "Read-only re-review changed the runner.");
            var reviewed = new CharacterCreationMagicResonanceReReviewConfirmRequest(
                review.Binding, selections, review.InitialPreview.PreviewDigest, "owner-magic-rereview", true, true);
            Check.That(reviewService.ConfirmReReview(fresh, reviewed with { ExplicitlyReviewedChanges = false }).Value is null
                && reviewService.ConfirmReReview(fresh, reviewed with { ExplicitlyConfirmed = false }).Value is null
                && reviewService.ConfirmReReview(fresh, reviewed with
                { PreviewDigest = review.InitialPreview.CurrentPreview.PreviewDigest }).Value is null
                && service.Confirm(fresh, new(review.Binding.Current, selections,
                    review.InitialPreview.CurrentPreview.PreviewDigest, "ordinary-bypass", true)).Value is null,
                "Re-review requires both confirmations and its separate preview; ordinary editing cannot bypass it.");
            Check.That(fixture.Snapshot(owner, id) == unchanged, "Rejected review changed the runner.");

            fixture.Authority.Transition(CreationFixture.AccountB);
            fixture.Authority.Transition(owner);
            probe.ResetCounts();
            Check.That(reviewService.LoadReReview(fresh, new(id)).Value is null
                && reviewService.PreviewReReview(fresh, new(review.Binding, selections)).Value is null
                && reviewService.ConfirmReReview(fresh, reviewed).Value is null
                && probe.Reads == 0 && probe.Mutations == 0 && fixture.Authority.ActiveLeases == 0,
                "Old-epoch review reached storage after owner ABA.");
            fresh = fixture.Authority.Capture();
            var prior = fixture.Read(owner, id).Value!;
            var result = reviewService.ConfirmReReview(fresh, reviewed);
            Check.That(result.Value is not null, "Re-review confirm: " + string.Join(",", result.Blockers));
            var saved = fixture.Read(owner, id).Value!;
            var draft = saved.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
            Check.That(saved.ContentRevision == prior.ContentRevision + 1
                && saved.SavedRevision == saved.ContentRevision && saved.Document.Content == prior.Document.Content
                && draft.AttributesDraftDigest == saved.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest
                && saved.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts?.Count == 2
                && JsonSerializer.Serialize(draft.Selections) == JsonSerializer.Serialize(review.HistoricalDraft.Selections),
                "Review must atomically checkpoint the same selection against current Attributes exactly once.");
            Check.That(service.Load(fresh, new(id)).Value is { CanEdit: true },
                "Reviewed Magic draft did not become editable.");
            var restarted = fixture.AddProvider(new FileWorkspaceStore(fixture.StateDirectory), coldOwner);
            var coldReview = restarted.GetRequiredService<IOwnerBoundCharacterCreationMagicResonanceReReviewService>();
            Check.That(restarted.GetRequiredService<IOwnerBoundCharacterCreationMagicResonanceService>()
                    .Load(coldOwner.Capture(), new(id)).Value is { CanEdit: true }
                && JsonSerializer.Serialize(coldReview.ConfirmReReview(coldOwner.Capture(), reviewed).Value)
                    == JsonSerializer.Serialize(result.Value), "Cold re-review receipt recovery failed.");
            ChangeBody(2);
            string later = fixture.Snapshot(owner, id);
            Check.That(JsonSerializer.Serialize(coldReview.ConfirmReReview(coldOwner.Capture(), reviewed).Value)
                    == JsonSerializer.Serialize(result.Value)
                && reviewService.ConfirmReReview(fresh, reviewed with { IdempotencyKey = "stale-new-command" }).Value is null
                && reviewService.PreviewReReview(fresh, new(review.Binding, selections)).Value is null
                && fixture.Snapshot(owner, id) == later,
                "Later saves must preserve exact historical recovery but reject a stale new command/preview.");
            foreach (var control in controls)
                Check.That(fixture.Snapshot(control.Key, id) == control.Value, "Re-review changed another owner.");
            Check.That(fixture.Authority.ActiveLeases == 0 && coldOwner.ActiveLeases == 0, "Re-review leaked a lease.");
        }

        void ChangeBody(int points)
        {
            CharacterCreationAttributeAllocation[] allocation = [new("BOD", points, 0)];
            var current = attributes.Load(fresh, new(id)).Value!;
            var next = attributes.Preview(fresh, new(current.Binding, allocation)).Value!;
            Check.That(next.CanConfirm, "Later Attributes: " + string.Join(",", next.Blockers));
            Check.That(attributes.Confirm(fresh, new(next.Binding, allocation, next.PreviewDigest, true)).Value is not null,
                "Later Attributes confirmation failed.");
        }

        void Reject(OwnerContextStamp invalid)
        {
            probe.ResetCounts();
            Check.That(service.Load(invalid, new(id)).Value is null
                && service.Preview(invalid, new(state.Binding, selections)).Value is null
                && service.Confirm(invalid, request).Value is null, "Magic admitted a stale/foreign owner.");
            Check.That(probe.Reads == 0 && probe.Mutations == 0 && fixture.Authority.ActiveLeases == 0,
                "Rejected Magic authority reached storage or leaked a lease.");
        }
    }
}
