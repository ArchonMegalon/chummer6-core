using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using System.Security.Cryptography;
using System.Text.Json;

namespace Chummer.Application.Characters;

public sealed partial class OwnerBoundCharacterCreationLifeModuleFinalizationService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, ICharacterFileQueries characterFiles,
    ICharacterSourceDataResolver sourceResolver, ILifeModulesCatalogService catalog)
    : IOwnerBoundCharacterCreationLifeModuleFinalizationService, IOwnerBoundCharacterCreationOverviewReader
{
    /// <summary>Evaluate a supplied snapshot through the existing isolated native
    /// read view. The calling store retains its owner/workspace leases and verifies
    /// currentness; this function cannot read storage or authorize a mutation.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationFoundationState> ReadNativeState(
        WorkspaceStoredDocument workspace, ICharacterFileQueries queries,
        ICharacterSourceDataResolver resolver, ILifeModulesCatalogService modules)
    {
        var view = new WorkspaceContinuationReadView(workspace);
        return new CharacterCreationFoundationService(view, queries, resolver, modules,
            new CharacterCreationFoundationDraftApplyAuthority(view)).LoadFinalizationState1966(workspace.Id);
    }

    public CharacterCreationOverviewRead? LoadOverview(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId, bool includePriorityDrafts)
        => OwnerBoundCharacterCreationFinalizationService.ReadOverview(
            store, ownerContext, characterFiles, sourceResolver,
            expectedOwner, workspaceId, includePriorityDrafts, catalog);

    public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId)
    {
        CharacterCreationFoundationResult<CharacterCreationFoundationState> Refused()
            => new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
        try
        {
            if (!expectedOwner.IsValid
                || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
                || store is not ICharacterCreationCurrentLifeModuleFinalizationCapability capability)
                return Refused();
            var result = capability.LoadCurrentLifeModuleFinalization(ownerContext, expectedOwner, workspaceId,
                characterFiles, sourceResolver, catalog);
            if (result?.Blockers is null) return Refused();
            if (result.Value is { } state)
            {
                if (result.Outcome != CharacterCreationFoundationOutcomes.Success
                    || state.Schema != CharacterCreationFoundationSchemas.SnapshotV1
                    || state.Binding is null || state.Binding.WorkspaceId != workspaceId
                    || state.AuthorityBlockers is null
                    || !state.AuthorityBlockers.SequenceEqual(result.Blockers, StringComparer.Ordinal)
                    || state.SnapshotDigest != "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        Schema = CharacterCreationFoundationSchemas.SnapshotV1, state.Binding, state.RulesetId,
                        Metatype = state.CurrentMetatype, state.BuildMethod, Created = state.CharacterCreated,
                        Metatypes = state.MetatypeOptions, Nationalities = state.NationalityOptions,
                        state.LifeModuleBudget, state.PendingDraft, Blockers = state.AuthorityBlockers
                    })))) return Refused();
            }
            else if (result.Outcome is not (CharacterCreationFoundationOutcomes.Missing or CharacterCreationFoundationOutcomes.Conflict
                or CharacterCreationFoundationOutcomes.Invalid or CharacterCreationFoundationOutcomes.Blocked)) return Refused();
            if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease)) return Refused();
            using (lease) return lease.Stamp == expectedOwner ? result : Refused();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Refused(); }
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationPreviewRequest request)
    {
        CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Refused()
            => new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);

        try
        {
            if (request?.Binding is null || !expectedOwner.IsValid
                || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
                || store is not ICharacterCreationCurrentLifeModuleFinalizationPreviewCapability capability)
                return Refused();
            var disposition = capability.PreviewCurrentLifeModuleFinalization(ownerContext, expectedOwner,
                request, out var result, CancellationToken.None);
            if (disposition == CurrentLifeModulePreviewDisposition.ProvenUnbound && result is null)
            {
                // This is a fresh native read, not reusable absence authority. Its
                // original owner stamp and Get reject intervening generation,
                // revision or binding activation changes before evaluation.
                return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.PreviewFinalization(request));
            }
            if (disposition != CurrentLifeModulePreviewDisposition.Preview || result?.Value is not { } preview
                || preview.Binding is null || preview.Compilation is null || preview.FinalizationBlocked is null
                || result.Blockers is null || preview.Schema != CharacterCreationFoundationSchemas.FinalizationPreviewV1
                || result.Outcome != (preview.FinalizationBlocked.Count == 0
                    ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked)
                || !result.Blockers.SequenceEqual(preview.FinalizationBlocked, StringComparer.Ordinal)
                || CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(preview.Binding)
                    != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(request.Binding)
                || preview.PreviewDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    preview with { PreviewDigest = string.Empty })
                || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
                return Refused();
            using (lease)
                return lease.Stamp == expectedOwner ? result : Refused();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Refused(); }
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationConfirmRequest request)
    {
        // Classify/dispatch before Invoke can create an ambient resolver scope.
        if (request?.Binding is null || store is not ICharacterCreationCurrentLifeModuleFinalizationCapability capability)
            return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            var result = capability.ConfirmCurrentLifeModuleFinalization(ownerContext, expectedOwner, request, null);
            if (result.Disposition == CurrentLifeModuleFinalizationDisposition.ProvenUnbound && result.Native is null)
                return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.ConfirmFinalization(request));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CurrentLifeModuleFinalizationReview? Review(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request)
    {
        try
        {
            if (request?.Binding is null || !expectedOwner.IsValid
                || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
                || store is not ICharacterCreationCurrentLifeModuleFinalizationCapability capability
                || capability.ReviewCurrentLifeModuleFinalization(ownerContext, expectedOwner, request, out var review)
                    != CurrentLifeModuleFinalizationDisposition.Reviewed
                || review is null || review.OwnerId != expectedOwner.Owner.NormalizedValue
                || review.TrustedLocalOwner != expectedOwner.Owner.IsLocalSingleUser
                || review.OwnerAuthorityInstanceId != expectedOwner.AuthorityInstanceId
                || review.OwnerTransitionRevision != expectedOwner.TransitionRevision
                || review.ReviewDigest != CharacterCreationFinalizationDigest.Compute(review with { ReviewDigest = string.Empty })
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(review.Request, request)
                || review.Preview.PreviewDigest != CharacterCreationFinalizationDigest.Compute(review.Preview with { PreviewDigest = string.Empty })
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(review.Preview.Binding, request.Binding)
                || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease)) return null;
            using (lease) return lease.Stamp == expectedOwner ? review : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    public CurrentLifeModuleFinalizationResult Confirm(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
        => InvokeBound(expectedOwner, request, review, lookup: false);

    public CurrentLifeModuleFinalizationResult LookupOriginal(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
        => InvokeBound(expectedOwner, request, review, lookup: true);

    private CurrentLifeModuleFinalizationResult InvokeBound(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review, bool lookup)
    {
        if (request?.Binding is null || review is null || !expectedOwner.IsValid
            || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
            || store is not ICharacterCreationCurrentLifeModuleFinalizationCapability capability)
            return new(CurrentLifeModuleFinalizationDisposition.Refused);
        CurrentLifeModuleFinalizationResult Unknown() => new(CurrentLifeModuleFinalizationDisposition.Unknown,
            OriginalIdentity: new(request.Binding.WorkspaceId, review.ReviewDigest,
                CharacterCreationLifeModuleFinalizationTransaction.CommandDigest(request)));
        try
        {
            var result = lookup
                ? capability.LookupCurrentLifeModuleFinalization(ownerContext, expectedOwner, request, review)
                : capability.ConfirmCurrentLifeModuleFinalization(ownerContext, expectedOwner, request, review);
            if (result.Disposition is CurrentLifeModuleFinalizationDisposition.Applied or CurrentLifeModuleFinalizationDisposition.Original)
            {
                if (result.Native?.Value is not { } native || result.Native.Outcome != CharacterCreationFoundationOutcomes.Success
                    || native.WorkspaceId != request.Binding.WorkspaceId || native.PreviousContentRevision != request.Binding.ContentRevision
                    || native.ContentRevision != request.Binding.ContentRevision + 1 || native.SavedRevision != native.ContentRevision
                    || native.PreviewDigest != request.PreviewDigest || native.DraftRevision != request.DraftRevision
                    || native.DraftDigest != request.DraftDigest || native.SourceDigest != request.Binding.SourceDigest
                    || !native.CharacterEffectsApplied || !native.CharacterCreated || !native.RequiresFreshCareerReopen
                    || result.Native.Blockers.Count != 0 || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
                    return Unknown();
                using (lease) return lease.Stamp == expectedOwner ? result : Unknown();
            }
            if (result.Disposition == CurrentLifeModuleFinalizationDisposition.Unknown)
                return Unknown();
            return (result.Disposition is CurrentLifeModuleFinalizationDisposition.Refused
                or CurrentLifeModuleFinalizationDisposition.NotFound or CurrentLifeModuleFinalizationDisposition.Unknown)
                && result.Native is null ? result : new(CurrentLifeModuleFinalizationDisposition.Refused);
        }
        // No retry: a response (including cancellation) can be lost after a real committed replacement.
        catch (Exception) { return Unknown(); }
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<CharacterCreationFoundationService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
        using (lease)
        {
            // Share parsed inputs only for this admitted synchronous operation.
            // Reuse still rechecks live bytes/catalog identity, including the
            // post-flush evaluation immediately before the atomic replacement.
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, id);
            return action(new(view, characterFiles, sourceScope ?? sourceResolver, catalog,
                new CharacterCreationFoundationDraftApplyAuthority(view)));
        }
    }
}
