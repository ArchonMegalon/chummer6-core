using Chummer.Application.Owners;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Review data only. The store reacquires owner and workspace authority.</summary>
public sealed record CurrentLifeModuleFinalizationReview(
    string OwnerId, bool TrustedLocalOwner, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    string IncarnationId, string SlotGeneration,
    string PredecessorDigest, string RetainedSourcesDigest,
    string CurrentEnvironmentDigest, string CaptureDigest,
    string ExecutionProfile, string ExecutionProfileFingerprint,
    CharacterCreationFoundationFinalizationPreviewRequest Request,
    CharacterCreationFoundationFinalizationPreview Preview, string ReviewDigest);

public enum CurrentLifeModuleFinalizationDisposition
{
    Refused = 0, ProvenUnbound, Reviewed, Applied, Original, NotFound, Unknown
}

/// <summary>Unknown requires explicit lookup of the same review and native command.</summary>
public sealed record CurrentLifeModuleFinalizationLookupIdentity(CharacterWorkspaceId WorkspaceId,
    string ReviewDigest, string CommandDigest);

public sealed record CurrentLifeModuleFinalizationResult(CurrentLifeModuleFinalizationDisposition Disposition,
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>? Native = null,
    CurrentLifeModuleFinalizationLookupIdentity? OriginalIdentity = null);

public interface ICharacterCreationCurrentLifeModuleFinalizationCapability
{
    // The store classifies and evaluates under the same owner/workspace leases.
    // Ambient services may be used only for a positively proven unbound record.
    // Neither a workspace nor retained execution authority leaves this operation.
    CharacterCreationLifeModuleCatalogProjection? LoadCurrentLifeModuleSuggestionCatalog(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationState expectedFoundation, ICharacterFileQueries characterFiles,
        ICharacterSourceDataResolver unboundResolver, ILifeModulesCatalogService unboundCatalog,
        CancellationToken cancellationToken = default) => null;

    CharacterCreationFoundationResult<CharacterCreationFoundationState>? LoadCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        ICharacterFileQueries characterFiles, ICharacterSourceDataResolver unboundResolver,
        ILifeModulesCatalogService unboundCatalog, CancellationToken cancellationToken = default) => null;

    CurrentLifeModuleFinalizationDisposition ReviewCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request,
        out CurrentLifeModuleFinalizationReview? review, CancellationToken cancellationToken = default);

    CurrentLifeModuleFinalizationResult ConfirmCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CurrentLifeModuleFinalizationReview? review, CancellationToken cancellationToken = default);

    CurrentLifeModuleFinalizationResult LookupCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CurrentLifeModuleFinalizationReview review, CancellationToken cancellationToken = default);
}

/// <summary>Native preparation output, never a resolver, capture or write capability.</summary>
public sealed class CharacterCurrentLifeModuleFinalizationPreparation
{
    internal CharacterCurrentLifeModuleFinalizationPreparation(WorkspaceDocument document,
        CharacterCreationFinalizationReceipt receipt) => (Document, Receipt) = (document, receipt);
    public WorkspaceDocument Document { get; }
    public CharacterCreationFinalizationReceipt Receipt { get; }
}
