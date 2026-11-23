using System.Diagnostics;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Runs the existing whole-build finalizer and all its nested evaluators against
/// one explicitly scoped store while the actual owner authority excludes transitions.
/// This does not owner-bind the separate domain editing services.
/// </summary>
public sealed class OwnerBoundCharacterCreationFinalizationService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterFileQueries characterQueries,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationFinalizationService,
    IOwnerBoundCharacterCreationOverviewReader
{
    private static readonly ActivitySource OverviewActivities =
        new(CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName);

    public CharacterCreationOverviewRead? LoadOverview(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId, bool includePriorityDrafts)
        => ReadOverview(store, ownerContext, characterQueries, sourceResolver,
            expectedOwner, workspaceId, includePriorityDrafts);

    internal static CharacterCreationOverviewRead? ReadOverview(
        IWorkspaceStore store, IOwnerContextAccessor ownerContext,
        ICharacterFileQueries characterQueries, ICharacterSourceDataResolver sourceResolver,
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId, bool includePriorityDrafts,
        ILifeModulesCatalogService? foundationCatalog = null)
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return null;

        using (lease)
        {
            // Reuse validated history only within this synchronous owner-bound overview.
            // Each lookup still hashes current inputs; source and owner admission stay live.
            using var historyScope = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            // This entire overview is synchronous under the original owner lease.
            // Compose the resolver's existing bounded read cache with its explicit
            // operation fence: context requests still admit fresh source bytes,
            // and disposal rechecks every observed input before returning results.
            // Ordinary readers and later requests retain their independent scopes.
            using ICharacterSourceDataResolverOperationScope? evaluationScope =
                (sourceScope as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver resolver = evaluationScope ?? sourceScope ?? sourceResolver;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                reuseReadObservation: true);
            var prerequisites = new CharacterCreationPrerequisiteService(view, characterQueries, resolver);
            var attributes = new CharacterCreationAttributesService(view, resolver);
            var qualities = new CharacterCreationQualitiesService(view, resolver, prerequisites, attributes);
            var magic = new CharacterCreationMagicResonanceService(view, resolver);
            var finalization = CreateFinalizer(view, characterQueries, resolver, prerequisites, attributes, qualities, magic);
            CharacterCreationFoundationResult<CharacterCreationFoundationState>? foundation = null;
            if (foundationCatalog is not null)
            {
                using var activity = OverviewActivities.StartActivity("overview-foundation");
                foundation = new CharacterCreationFoundationService(view, characterQueries, resolver, foundationCatalog,
                    new CharacterCreationFoundationDraftApplyAuthority(view)).Load(new(workspaceId));
            }

            // Unscoped evaluator entry points are safe only against this private
            // admitted view: it redirects every read and capability check to the
            // actual owner. Every resolver lookup still validates live sources.
            // No mutation API is invoked and no view/context survives the lease.
            CharacterCreationContactResult<CharacterCreationContactsState> contactsRead;
            using (OverviewActivities.StartActivity("overview-contacts"))
                contactsRead = new CharacterCreationContactsService(view, resolver, characterQueries).Load(new(workspaceId));
            CharacterCreationLifestyleResult<CharacterCreationLifestylesState> lifestylesRead;
            using (OverviewActivities.StartActivity("overview-lifestyles"))
                lifestylesRead = new CharacterCreationLifestylesService(view, resolver).Load(new(workspaceId));
            CharacterCreationFinalizationResult<CharacterCreationFinalizationState> finalizationRead;
            CharacterCreationFoundationResult<CharacterCreationQualitiesState>? qualitiesRead;
            CharacterCreationFoundationResult<CharacterCreationMagicResonanceState>? magicRead;
            using (OverviewActivities.StartActivity("overview-finalization"))
                finalizationRead = finalization.LoadForOverview(new(workspaceId),
                    out qualitiesRead, out magicRead);
            // Present drafts were just evaluated by the full finalizer. Return
            // those exact results, not another expensive pass through the same
            // catalogs. This is local to this admitted read, never a save cache.
            // If finalization did not load a domain, its overview still needs the
            // real domain loader (including its failures and available choices).
            if (includePriorityDrafts)
            {
                if (qualitiesRead is null)
                {
                    using var activity = OverviewActivities.StartActivity("overview-qualities");
                    qualitiesRead = qualities.Load(new(workspaceId));
                }
                if (magicRead is null)
                {
                    using var activity = OverviewActivities.StartActivity("overview-magic");
                    magicRead = magic.Load(new(workspaceId));
                }
            }
            return new(
                contactsRead,
                includePriorityDrafts ? qualitiesRead : null,
                includePriorityDrafts ? magicRead : null,
                lifestylesRead,
                finalizationRead) { Foundation = foundation };
        }
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request), reuseReadObservation: true);
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Review(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationFinalizationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationFinalizationService, CharacterCreationFinalizationResult<T>> action,
        bool reuseReadObservation = false)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFinalizationOutcomes.Unavailable, null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver operationResolver = sourceScope ?? sourceResolver;
            // Never cache this graph or resolve unbound singleton domain services:
            // Skills/Magic construct Attributes internally and Qualities calls both
            // Prerequisite and Attributes. Every one must share this same view.
            // Only Load reuses its complete validated read. Review, Confirm and
            // receipt lookup retain fresh reads; nothing survives this lease.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId, reuseReadObservation);
            var prerequisites = new CharacterCreationPrerequisiteService(view, characterQueries, operationResolver);
            var attributes = new CharacterCreationAttributesService(view, operationResolver);
            var service = CreateFinalizer(view, characterQueries, operationResolver, prerequisites, attributes,
                new CharacterCreationQualitiesService(view, operationResolver, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(view, operationResolver));
            // Includes idempotency reads, the durable CAS and postcommit receipt
            // observation. No await or postcommit owner recapture may split it.
            return action(service);
        }
    }

    private static CharacterCreationFinalizationService CreateFinalizer(
        IWorkspaceStore view, ICharacterFileQueries characterQueries, ICharacterSourceDataResolver resolver,
        CharacterCreationPrerequisiteService prerequisites, CharacterCreationAttributesService attributes,
        CharacterCreationQualitiesService qualities, CharacterCreationMagicResonanceService magic)
        => new(view, characterQueries, prerequisites, attributes,
            new CharacterCreationSkillsService(view, resolver), qualities, magic,
            new CharacterCreationResourcesService(view, resolver),
            new CharacterCreationGearService(view, resolver), resolver);
}
